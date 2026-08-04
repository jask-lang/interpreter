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
            Expression.MapLiteral   ml => EvaluateMapLiteral(ml),
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
        var evaluatedArgs = new List<Expression>(call.Arguments.Count);
        foreach (var arg in call.Arguments)
        {
            evaluatedArgs.Add(new Expression.Literal(Evaluate(arg)));
        }

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

        var evaluatedArgs = new List<(Token ParamName, Expression Value)>(call.Args.Count);
        foreach (var arg in call.Args)
        {
            evaluatedArgs.Add((arg.ParamName, new Expression.Literal(Evaluate(arg.Value))));
        }

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
        bool hasUserOverload = _functionOverloads.ContainsKey(funcName);

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

        if (_functionOverloads.TryGetValue(funcName, out var overloads) == false || overloads.Count == 0)
        {
            throw new LangException($"Unknown function '{funcName}'", funcExpr.Name.Line, _filePath);
        }

        var bestMatch = SelectBestOverload(overloads, argValues);
        if (bestMatch == null)
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

        var (parameters, body) = bestMatch.Value;

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
        if (_functionOverloads.TryGetValue(name, out var overloads) == false || overloads.Count == 0)
            throw new LangException($"Unknown function '{name}'", call.Name.Line, _filePath);

        // evaluate args up front so we can match on types too
        var evaluatedArgs = new List<(Token ParamName, object? Value)>(call.Args.Count);
        foreach (var arg in call.Args)
        {
            evaluatedArgs.Add((arg.ParamName, Evaluate(arg.Value)));
        }

        var suppliedParamNames = new HashSet<string>(call.Args.Count);
        foreach (var arg in call.Args)
        {
            suppliedParamNames.Add(arg.ParamName.Lexeme);
        }

        var match = SelectBestNamedOverload(overloads, evaluatedArgs, suppliedParamNames);
        if (match == null)
        {
            throw new LangException($"Function '{name}' has no overload matching named parameters ({string.Join(", ", suppliedParamNames)})", call.Name.Line, _filePath);
        }

        // bind in parameter declaration order, filling defaults for omitted params
        var functionEnv = new Dictionary<string, object?>();
        foreach (var param in match.Value.Params)
        {
            var found = false;
            object? suppliedValue = null;
            foreach (var supplied in evaluatedArgs)
            {
                if (supplied.ParamName.Lexeme == param.Name.Lexeme)
                {
                    found = true;
                    suppliedValue = supplied.Value;
                    break;
                }
            }

            if (found)
            {
                functionEnv[param.Name.Lexeme] = suppliedValue;
            }
            else if (param.Item3 != null)
            {
                functionEnv[param.Name.Lexeme] = Evaluate(param.Item3);
            }
        }

        _scopes.Push(functionEnv);
        try
        {
            foreach (var stmt in match.Value.Body)
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

    private (List<(Token Name, Token Type, JaskLang.Expression? Default)> Params, List<Statement> Body)? SelectBestOverload(
        IReadOnlyList<(List<(Token Name, Token Type, JaskLang.Expression? Default)> Params, List<Statement> Body)> overloads,
        IReadOnlyList<object?> argValues)
    {
        (List<(Token Name, Token Type, JaskLang.Expression? Default)> Params, List<Statement> Body)? bestMatch = null;
        int bestRequired = -1;
        int bestAnyCount = int.MaxValue;

        foreach (var overload in overloads)
        {
            int required = 0;
            int paramCount = overload.Params.Count;
            if (argValues.Count < 0 || argValues.Count > paramCount)
            {
                continue;
            }

            int providedArgCount = argValues.Count;
            bool matches = true;
            for (int i = 0; i < providedArgCount; i++)
            {
                var parameter = overload.Params[i];
                if (parameter.Item3 == null)
                {
                    required++;
                }

                if (!IsValueOfType(argValues[i], parameter.Type.Lexeme))
                {
                    matches = false;
                    break;
                }
            }

            if (!matches)
            {
                continue;
            }

            if (argValues.Count < required)
            {
                continue;
            }

            int anyCount = 0;
            for (int i = 0; i < paramCount; i++)
            {
                if (overload.Params[i].Type.Lexeme == "any")
                {
                    anyCount++;
                }
            }

            if (required > bestRequired || (required == bestRequired && anyCount < bestAnyCount))
            {
                bestMatch = overload;
                bestRequired = required;
                bestAnyCount = anyCount;
            }
        }

        return bestMatch;
    }

    private (List<(Token Name, Token Type, JaskLang.Expression? Default)> Params, List<Statement> Body)? SelectBestNamedOverload(
        IReadOnlyList<(List<(Token Name, Token Type, JaskLang.Expression? Default)> Params, List<Statement> Body)> overloads,
        IReadOnlyList<(Token ParamName, object? Value)> evaluatedArgs,
        HashSet<string> suppliedParamNames)
    {
        (List<(Token Name, Token Type, JaskLang.Expression? Default)> Params, List<Statement> Body)? bestMatch = null;
        int bestRequired = -1;
        int bestAnyCount = int.MaxValue;

        foreach (var overload in overloads)
        {
            int required = 0;
            int paramCount = overload.Params.Count;
            if (evaluatedArgs.Count < 0 || evaluatedArgs.Count > paramCount)
            {
                continue;
            }

            bool hasValidNames = true;
            var parameterNames = new HashSet<string>(paramCount);
            for (int i = 0; i < paramCount; i++)
            {
                var parameter = overload.Params[i];
                if (parameter.Item3 == null)
                {
                    required++;
                }
                parameterNames.Add(parameter.Name.Lexeme);
            }

            if (!parameterNames.IsSupersetOf(suppliedParamNames))
            {
                continue;
            }

            foreach (var parameter in overload.Params)
            {
                bool found = false;
                foreach (var supplied in evaluatedArgs)
                {
                    if (supplied.ParamName.Lexeme == parameter.Name.Lexeme)
                    {
                        found = true;
                        if (!IsValueOfType(supplied.Value, parameter.Type.Lexeme))
                        {
                            hasValidNames = false;
                            break;
                        }
                        break;
                    }
                }

                if (!found)
                {
                    if (parameter.Item3 == null)
                    {
                        hasValidNames = false;
                        break;
                    }
                }
            }

            if (!hasValidNames || evaluatedArgs.Count < required)
            {
                continue;
            }

            int anyCount = 0;
            for (int i = 0; i < paramCount; i++)
            {
                if (overload.Params[i].Type.Lexeme == "any")
                {
                    anyCount++;
                }
            }

            if (required > bestRequired || (required == bestRequired && anyCount < bestAnyCount))
            {
                bestMatch = overload;
                bestRequired = required;
                bestAnyCount = anyCount;
            }
        }

        return bestMatch;
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

    private object? EvaluateMapLiteral(Expression.MapLiteral ml)
    {
        var map = new Dictionary<object, object>(ml.Entries.Count);

        foreach (var (key, valueExpr) in ml.Entries)
        {
            if (key.Literal is not string keyStr)
            {
                throw new LangException($"Map literal keys must be string literals", key.Line, _filePath);
            }

            object? value = Evaluate(valueExpr);
            if (value is null)
            {
                throw new LangException($"Map literal values cannot be nil", key.Line, _filePath);
            }

            if (map.ContainsKey(keyStr))
            {
                throw new LangException($"Map literal has duplicate key '{keyStr}'", key.Line, _filePath);
            }

            map[keyStr] = value;
        }

        return map;
    }
}