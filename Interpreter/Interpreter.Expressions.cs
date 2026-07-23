namespace JaskLang;

public partial class Interpreter
{
    private object? Evaluate(Expression expression)
    {
        return expression switch
        {
            Expression.Literal      l => l.Value,
            Expression.Grouping     g => Evaluate(g.Inner),
            Expression.Variable     v => LookupVariable(v.Name),
            Expression.Unary        u => EvaluateUnary(u),
            Expression.Binary       b => EvaluateShortCircuit(b),
            Expression.Call         c => EvaluateCall(c),
            Expression.NamedCall   nc => EvaluateNamedCall(nc),
            Expression.ModuleCall  mc => EvaluateModuleCall(mc),
            Expression.ModuleNamedCall mnc => EvaluateModuleNamedCall(mnc),
            Expression.StructCall  sc => EvaluateStructCall(sc),
            Expression.MemberAccess m => EvaluateMemberAccess(m),
            _ => throw new LangException($"Unknown expression: {expression}")
        };
    }

    // handles and/or with short-circuit evaluation, delegates everything else to EvaluateBinary
    private object? EvaluateShortCircuit(Expression.Binary b)
    {
        if (b.Op.Type == TokenType.And)
        {
            object? left = Evaluate(b.Left);
            if (!IsTruthy(left)) return false; // short-circuit: left is false, skip right
            return IsTruthy(Evaluate(b.Right));
        }

        if (b.Op.Type == TokenType.Or)
        {
            object? left = Evaluate(b.Left);
            if (IsTruthy(left)) return true; // short-circuit: left is true, skip right
            return IsTruthy(Evaluate(b.Right));
        }

        return EvaluateBinary(b);
    }

    private object? EvaluateModuleCall(Expression.ModuleCall call)
    {
        if (_modules.TryGetValue(call.ModuleAlias.Lexeme, out var module) == false)
        {
            throw new LangException(
                $"Unknown module '{call.ModuleAlias.Lexeme}'. Did you forget a 'use ... as {call.ModuleAlias.Lexeme}' statement?",
                call.ModuleAlias.Line, _filePath);
        }

        // arguments are expressions written in the callers scope, so evaluate them here first,
        // then hand the modules own interpreter already-evaluated values wrapped as literals
        var evaluatedArgs = call.Arguments
            .Select(a => (Expression)new Expression.Literal(Evaluate(a)))
            .ToList();

        var innerCall = new Expression.Call(new Expression.Variable(call.Name), evaluatedArgs);
        return module.EvaluateCall(innerCall);
    }

    private object? EvaluateModuleNamedCall(Expression.ModuleNamedCall call)
    {
        if (_modules.TryGetValue(call.ModuleAlias.Lexeme, out var module) == false)
        {
            throw new LangException(
                $"Unknown module '{call.ModuleAlias.Lexeme}'. Did you forget a 'use ... as {call.ModuleAlias.Lexeme}' statement?",
                call.ModuleAlias.Line, _filePath);
        }

        var evaluatedArgs = call.Args
            .Select(a => (a.ParamName, Value: (Expression)new Expression.Literal(Evaluate(a.Value))))
            .ToList();

        var innerCall = new Expression.NamedCall(call.Name, evaluatedArgs);
        return module.EvaluateNamedCall(innerCall);
    }

    private object? EvaluateCall(Expression.Call call)
    {
        Expression.Variable? funcExpr = call.Callee as Expression.Variable;
        if (funcExpr == null)
        {
            throw new LangException("Can only call functions by name");
        }

        string funcName = funcExpr.Name.Lexeme;

        // check for user-defined overloads first (they take priority over internals)
        bool hasUserOverload = _functions.Keys.Any(k => k.StartsWith(funcName + "("));

        // if no user overload exists, delegate to internal immediately (avoids double arg evaluation)
        if (!hasUserOverload && _internalFunctions.TryGetValue(funcName, out var internalFunc))
        {
            return internalFunc(call);
        }

        // new struct with no overwritten fields: MyStruct()
        if (_structs.TryGetValue(funcName, out var structBody))
        {
            if (call.Arguments.Count != 0)
            {
                throw new LangException($"Struct '{funcName}' instantiation with positional arguments is not supported. Use named fields: {funcName}(field = value, ...)", funcExpr.Name.Line, _filePath);
            }

            var fields = new Dictionary<string, object?>();

            // run the struct body in a temporary scope, capturing set results as fields
            _scopes.Push(new Dictionary<string, object?>());
            try
            {
                foreach (var stmt in structBody)
                {
                    Execute(stmt);
                }
                // copy everything set in that scope into the fields dictionary
                foreach (var kv in _scopes.Peek())
                {
                    fields[kv.Key] = kv.Value;
                }
            }
            finally
            {
                _scopes.Pop();
            }

            return new StructInstance(funcName, fields);
        }

        // evaluate arguments first so we can match overloads by compatible types
        // if a parameter evaluates to nil, throw an error (jask does not allow passing nil to functions)
        var argValues = new List<object?>();
        foreach (var arg in call.Arguments)
        {
            var value = Evaluate(arg);
            if (value != null)
            {
                argValues.Add(value);
            }
            else
            {
                throw new LangException($"Passed parameter for function '{funcName}' evaluated to nil.", funcExpr.Name.Line, _filePath);
            }
        }

        var overloads = _functions
            .Where(kv => kv.Key.StartsWith(funcName + "("))
            .Select(kv => kv.Value)
            .ToList();

        if (overloads.Count == 0)
        {
            throw new LangException($"Unknown function '{funcName}'", funcExpr.Name.Line, _filePath);
        }

        // pick best overload: required params = those without defaults
        // match if argCount in [requiredParams, totalParams] and types compatible
        var match = overloads
            .Select(o => (o, required: o.Params.Count(p => p.Item3 == null)))
            .Where(x => argValues.Count >= x.required && argValues.Count <= x.o.Params.Count)
            .Where(x => x.o.Params.Zip(argValues, (p, v) => IsValueOfType(v, p.Type.Lexeme)).All(z => z))
            .OrderByDescending(x => x.required)
            .ThenBy(x => x.o.Params.Count(p => p.Type.Lexeme == "any"))
            .Select(x => x.o)
            .FirstOrDefault();

        if (match == default)
        {
            // no user overload matched — fall back to internal if one exists
            if (_internalFunctions.TryGetValue(funcName, out var internalFunction))
            {
                return internalFunction(call);
            }

            bool anyArity = overloads.Any(o => o.Params.Count >= argValues.Count && o.Params.Count(p => p.Item3 == null) <= argValues.Count);
            if (anyArity)
            {
                throw new LangException($"Function '{funcName}' has no overload matching types ({string.Join(", ", argValues.Select(GetValueType))})", funcExpr.Name.Line, _filePath);
            }

            throw new LangException($"Function '{funcName}' has no overload that takes {argValues.Count} argument(s)", funcExpr.Name.Line, _filePath);
        }

        var (parameters, body) = match;

        var functionEnv = new Dictionary<string, object?>();

        // bind supplied arguments to leading parameters
        for (int i = 0; i < argValues.Count; i++)
        {
            functionEnv[parameters[i].Name.Lexeme] = argValues[i];
        }

        // bind defaults for remaining parameters
        for (int i = argValues.Count; i < parameters.Count; i++)
        {
            if (parameters[i].Item3 != null)
            {
                // Item3 cannot be null here so we can use null-forgiving
                functionEnv[parameters[i].Name.Lexeme] = Evaluate(parameters[i].Item3!);
            }
        }

        // call function body in the new environment
        _scopes.Push(functionEnv);
        try
        {
            foreach (var stmt in body)
            {
                Execute(stmt);
            }
        }
        catch (ReturnException ex)
        {
            return ex.Value;
        }
        finally
        {
            _scopes.Pop();
        }

        // function returns nil
        return null;
    }

    private object? EvaluateNamedCall(Expression.NamedCall call)
    {
        string name = call.Name.Lexeme;

        // check if it's an internal function
        if (_internalFunctions.TryGetValue(name, out var internalFunc))
        {
            // get expected parameter names for this internal function
            var paramNames = GetInternalFunctionParameterNames(name);
            
            // verify all supplied parameter names are valid
            var suppliedNames = call.Args.Select(a => a.ParamName.Lexeme).ToList();
            foreach (var suppliedName in suppliedNames)
            {
                if (!paramNames.Contains(suppliedName))
                {
                    throw new LangException($"Function '{name}' has no parameter named '{suppliedName}'", call.Name.Line, _filePath);
                }
            }
            
            // verify all required parameters are supplied
            if (suppliedNames.Count != paramNames.Count)
            {
                throw new LangException($"Function '{name}' expects {paramNames.Count} argument(s), but got {suppliedNames.Count}", call.Name.Line, _filePath);
            }
            
            // reorder arguments to match the expected parameter order
            var reorderedArgs = new List<Expression>();
            foreach (var paramName in paramNames)
            {
                var argIndex = call.Args.FindIndex(a => a.ParamName.Lexeme == paramName);
                reorderedArgs.Add(call.Args[argIndex].Value);
            }
            
            // create a regular Call expression with reordered arguments
            var regularCall = new Expression.Call(new Expression.Variable(call.Name), reorderedArgs);
            return internalFunc(regularCall);
        }

        // if it's a struct, delegate to struct instantiation
        if (_structs.ContainsKey(name))
        {
            var fieldInits = call.Args.Select(a => (a.ParamName, a.Value)).ToList();
            return EvaluateStructCall(new Expression.StructCall(call.Name, fieldInits));
        }

        // find all overloads for this name
        var overloads = _functions
            .Where(kv => kv.Key.StartsWith(name + "("))
            .Select(kv => kv.Value)
            .ToList();

        if (overloads.Count == 0)
            throw new LangException($"Unknown function '{name}'", call.Name.Line, _filePath);

        // evaluate args up front so we can match on types too
        var evaluatedArgs = call.Args
            .Select(a => (a.ParamName, Value: Evaluate(a.Value)))
            .ToList();

        var suppliedParamNames = call.Args.Select(a => a.ParamName.Lexeme).ToHashSet();

        // find overload: supplied names must be a subset of param names,
        // and at least all required (non-default) params must be supplied
        var match = overloads
            .Select(o => (o, required: o.Params.Count(p => p.Item3 == null)))
            .Where(x => call.Args.Count >= x.required && call.Args.Count <= x.o.Params.Count)
            .Where(x => x.o.Params.Select(p => p.Name.Lexeme).ToHashSet().IsSupersetOf(suppliedParamNames))
            .Where(x => x.o.Params.All(p =>
            {
                if (!evaluatedArgs.Any(a => a.ParamName.Lexeme == p.Name.Lexeme))
                {
                    // missing arg is OK only if default exists
                    return p.Item3 != null;
                }

                var arg = evaluatedArgs.First(a => a.ParamName.Lexeme == p.Name.Lexeme);
                return IsValueOfType(arg.Value, p.Type.Lexeme);
            }))
            .OrderByDescending(x => x.required)
            .ThenBy(x => x.o.Params.Count(p => p.Type.Lexeme == "any"))
            .Select(x => x.o)
            .FirstOrDefault();

        if (match == default)
        {
            throw new LangException($"Function '{name}' has no overload matching named parameters ({string.Join(", ", suppliedParamNames)})", call.Name.Line, _filePath);
        }

        // bind in parameter declaration order, filling defaults for omitted params
        var functionEnv = new Dictionary<string, object?>();
        foreach (var param in match.Params)
        {
            var supplied = evaluatedArgs.FirstOrDefault(a => a.ParamName.Lexeme == param.Name.Lexeme);
            var found = !supplied.Equals(default);
            if (found)
            {
                functionEnv[param.Name.Lexeme] = supplied.Value;
            }
            else if (param.Item3 != null)
            {
                functionEnv[param.Name.Lexeme] = Evaluate(param.Item3);
            }
        }

        _scopes.Push(functionEnv);
        try
        {
            foreach (var stmt in match.Body)
            {
                Execute(stmt);
            }
        }
        catch (ReturnException ex)
        {
            return ex.Value;
        }
        finally
        {
            _scopes.Pop();
        }

        return null;
    }

    private object? EvaluateStructCall(Expression.StructCall call)
    {
        string structName = call.Name.Lexeme;

        if (!_structs.TryGetValue(structName, out var structBody))
        {
            throw new LangException($"Unknown struct '{structName}'", call.Name.Line, _filePath);
        }

        // run the body to get default field values
        var fields = new Dictionary<string, object?>();
        _scopes.Push(new Dictionary<string, object?>());

        try
        {
            foreach (var stmt in structBody)
            {
                Execute(stmt);
            }

            foreach (var kv in _scopes.Peek())
            {
                fields[kv.Key] = kv.Value;
            }
        }
        finally
        {
            _scopes.Pop();
        }

        // apply named field initializers, validating each field name
        foreach (var (field, valueExpr) in call.FieldInits)
        {
            if (!fields.ContainsKey(field.Lexeme))
            {
                throw new LangException($"Struct '{structName}' has no field '{field.Lexeme}'", field.Line, _filePath);
            }

            fields[field.Lexeme] = Evaluate(valueExpr);
        }

        return new StructInstance(structName, fields);
    }

    private object? EvaluateMemberAccess(Expression.MemberAccess m)
    {
        object? obj = Evaluate(m.Struct);

        if (obj is not StructInstance instance)
        {
            throw new LangException($"Attempted to access member '{m.Member.Lexeme}' on a non-struct value (got '{GetValueType(obj)}')", m.Member.Line, _filePath);
        }

        if (!instance.Fields.TryGetValue(m.Member.Lexeme, out var fieldValue))
        {
            throw new LangException($"Struct '{instance.TypeName}' has no member '{m.Member.Lexeme}'", m.Member.Line, _filePath);
        }

        return fieldValue;
    }

    private object EvaluateUnary(Expression.Unary u)
    {
        object? right = Evaluate(u.Right);
        return u.Op.Type switch
        {
            TokenType.Minus => -CheckNumber(u.Op, right),
            TokenType.Not   => !IsTruthy(right),
            _ => throw new LangException($"Unknown unary operator '{u.Op.Lexeme}'.", u.Op.Line, _filePath)
        };
    }

    private object EvaluateBinary(Expression.Binary b)
    {
        object? left = Evaluate(b.Left);
        object? right = Evaluate(b.Right);

        switch (b.Op.Type)
        {
            case TokenType.Plus:
                // add two numbers, otherwise concatenate (e.g. for strings)
                if (left is double ld && right is double rd)
                {
                    return ld + rd;
                }

                return Stringify(left) + Stringify(right);

            case TokenType.Minus:            return CheckNumber(b.Op, left) - CheckNumber(b.Op, right);
            case TokenType.Star:             return CheckNumber(b.Op, left) * CheckNumber(b.Op, right);
            case TokenType.Modulo:           return CheckNumber(b.Op, left) % CheckNumber(b.Op, right);
            case TokenType.Greater:          return CheckNumber(b.Op, left) > CheckNumber(b.Op, right);
            case TokenType.GreaterEqual:     return CheckNumber(b.Op, left) >= CheckNumber(b.Op, right);
            case TokenType.Less:             return CheckNumber(b.Op, left) < CheckNumber(b.Op, right);
            case TokenType.LessEqual:        return CheckNumber(b.Op, left) <= CheckNumber(b.Op, right);
            case TokenType.EqualEqual:       return IsEqual(left, right);
            case TokenType.Is:               return AreSameObject(left, right);
            case TokenType.BangEqual:        return !IsEqual(left, right);
            case TokenType.Slash:
                double divisor = CheckNumber(b.Op, right);
                if (divisor == 0)
                {
                    throw new LangException("Division by zero", b.Op.Line, _filePath);
                }

                return CheckNumber(b.Op, left) / divisor;

            default:
                throw new LangException($"Unknown operator '{b.Op.Lexeme}'", b.Op.Line, _filePath);
        }
    }

    private object? LookupVariable(Token name)
    {
        foreach (var scope in _scopes)
        {
            if (scope.TryGetValue(name.Lexeme, out var value)) {
                if (value is RestrictedValue) return ((RestrictedValue)value).Value;
                return value;
            }
        }

        throw new LangException($"Unknown variable '{name.Lexeme}'.", name.Line, _filePath);
    }

    private static bool IsEqual(object? a, object? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        return AreValuesEqual(a, b);
    }

    private static bool AreSameObject(object? a, object? b)
    {
        if (a is null && b is null)
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return ReferenceEquals(a, b);
    }

    private static bool IsTruthy(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is bool b)
        {
            return b;
        }

        return true;
    }
}