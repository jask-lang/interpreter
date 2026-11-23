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
            Expression.MapIndex     mi => EvaluateMapIndex(mi),
            Expression.ListLiteral  ll => EvaluateListLiteral(ll),
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

        string name = call.Name.Lexeme;
        bool isUserDefinedFunction = module._functionOverloads.ContainsKey(name);
        bool isUserDefinedStruct = module._structs.ContainsKey(name);

        if (isUserDefinedFunction && !module._exportedFunctionNames.Contains(name))
        {
            throw new LangException(
                $"Function '{name}' is not exported by module '{call.ModuleAlias.Lexeme}'",
                call.Name.Line, _filePath);
        }

        if (isUserDefinedStruct && !module._exportedStructNames.Contains(name))
        {
            throw new LangException(
                $"Struct '{name}' is not exported by module '{call.ModuleAlias.Lexeme}'",
                call.Name.Line, _filePath);
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

        string name = call.Name.Lexeme;
        bool isUserDefinedFunction = module._functionOverloads.ContainsKey(name);
        bool isUserDefinedStruct = module._structs.ContainsKey(name);

        if (isUserDefinedFunction && !module._exportedFunctionNames.Contains(name))
        {
            throw new LangException(
                $"Function '{name}' is not exported by module '{call.ModuleAlias.Lexeme}'",
                call.Name.Line, _filePath);
        }

        if (isUserDefinedStruct && !module._exportedStructNames.Contains(name))
        {
            throw new LangException(
                $"Struct '{name}' is not exported by module '{call.ModuleAlias.Lexeme}'",
                call.Name.Line, _filePath);
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

        // resolve the call site against the current function table and cache the
        // result on the AST node, so repeated invocations (e.g. inside loops) skip
        // the dictionary lookups and overload selection entirely
        CallResolution? resolution = call.CallResolution;
        int generation = _resolutionGeneration;

        if (resolution is null || call.CallResolutionGeneration != generation)
        {
            resolution = ResolveCall(funcName);
            call.CallResolution = resolution;
            call.CallResolutionGeneration = generation;
        }

        // no user overload exists, delegate to internal
        if (resolution.IsInternalOnly)
        {
            return resolution.Internal!(call);
        }

        // new struct — clone cached defaults directly
        if (resolution.HasStruct)
        {
            if (call.Arguments.Count == 0)
            {
                var fields = new Dictionary<string, object?>(resolution.StructDefaults!);
                return new StructInstance(funcName, fields);
            }

            throw new LangException($"Struct '{funcName}' instantiation with positional arguments is not supported. Use named fields: {funcName}(field = value, ...)", funcExpr.Name.Line, _filePath);
        }

        // evaluate arguments first so we can match overloads by compatible types
        // if a parameter evaluates to nil, throw an error (jask does not allow passing nil to functions)
        var argValues = new List<object?>(call.Arguments.Count);
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

        if (resolution.HasUserOverloads == false)
        {
            // no user overload matched — fall back to internal if one exists
            if (resolution.Internal != null)
            {
                return resolution.Internal(call);
            }

            throw new LangException($"Unknown function '{funcName}'", funcExpr.Name.Line, _filePath);
        }

        var bestMatch = resolution.Single != null
            ? MatchSingleOverload(resolution.Single, argValues)
            : SelectBestOverload(resolution.Candidates!, argValues);

        if (bestMatch != null)
        {
            return InvokeUserFunction(bestMatch, argValues, funcName, funcExpr.Name);
        }

        // no user overload matched — fall back to internal if one exists
        if (resolution.Internal != null)
        {
            return resolution.Internal(call);
        }

        bool anyArity = false;
        foreach (var overload in resolution.Candidates!)
        {
            if (overload.ParamCount >= argValues.Count && overload.RequiredCount <= argValues.Count)
            {
                anyArity = true;
                break;
            }
        }

        if (anyArity)
        {
            throw new LangException($"Function '{funcName}' has no overload matching types ({string.Join(", ", argValues.Select(GetValueType))})", funcExpr.Name.Line, _filePath);
        }

        throw new LangException($"Function '{funcName}' has no overload that takes {argValues.Count} argument(s)", funcExpr.Name.Line, _filePath);
    }

    // fast path for call sites whose name resolves to exactly one user overload
    private UserFunctionInfo? MatchSingleOverload(UserFunctionInfo overload, IReadOnlyList<object?> argValues)
    {
        if (argValues.Count > overload.ParamCount)
        {
            return null;
        }

        int required = 0;
        for (int i = 0; i < argValues.Count; i++)
        {
            if (overload.IsDefault[i] == false)
            {
                required++;
            }

            if (overload.IsAny[i] == false && IsValueOfType(argValues[i], overload.TypeNames[i]) == false)
            {
                return null;
            }
        }

        if (argValues.Count < required)
        {
            return null;
        }

        return overload;
    }

    // binds arguments to the matched function and executes its body
    private object? InvokeUserFunction(UserFunctionInfo function, List<object?> argValues, string funcName, Token nameToken)
    {
        var functionEnv = RentFunctionScope();
        var parameters = function.Params;

        // bind supplied arguments to leading parameters
        for (int i = 0; i < argValues.Count; i++)
        {
            functionEnv[parameters[i].Name.Lexeme] = argValues[i];
        }

        // bind defaults for remaining parameters
        for (int i = argValues.Count; i < parameters.Count; i++)
        {
            if (function.IsDefault[i])
            {
                functionEnv[parameters[i].Name.Lexeme] = Evaluate(parameters[i].Item3!);
            }
        }

        // check that all required parameters were bound (before entering body)
        for (int i = 0; i < parameters.Count; i++)
        {
            if (function.IsDefault[i] == false && functionEnv.ContainsKey(parameters[i].Name.Lexeme) == false)
            {
                ReturnFunctionScope(functionEnv);
                throw new LangException($"Missing required parameter '{parameters[i].Name.Lexeme}' when calling '{funcName}'", nameToken.Line, _filePath);
            }
        }

        return ExecuteFunctionBody(function, functionEnv);
    }

    private object? ExecuteFunctionBody(UserFunctionInfo function, Dictionary<string, object?> functionEnv)
    {
        // execute the function body with isolated return state
        object? savedReturnValue = _returnValue;
        bool savedReturning = _returning;

        _scopes.Add(functionEnv);
        _returnValue = null;
        _returning = false;
        foreach (var stmt in function.Body)
        {
            Execute(stmt);
            if (_returning) break;
        }
        var result = _returnValue;
        _scopes.RemoveAt(_scopes.Count - 1);
        ReturnFunctionScope(functionEnv);

        // restore callers return state (nested calls during this entire call can clobber it)
        _returnValue = savedReturnValue;
        _returning = savedReturning;
        return result;
    }

    private object? EvaluateNamedCall(Expression.NamedCall call)
    {
        string name = call.Name.Lexeme;

        // resolve the call site (same caching as EvaluateCall)
        CallResolution? resolution = call.CallResolution;
        int generation = _resolutionGeneration;

        if (resolution is null || call.CallResolutionGeneration != generation)
        {
            resolution = ResolveCall(name);
            call.CallResolution = resolution;
            call.CallResolutionGeneration = generation;
        }

        // dispatch to internal function (named args supported via param-name reordering)
        if (resolution.Internal != null)
        {
            return InvokeInternalNamedFunction(resolution.Internal, name, call);
        }

        // if it's a struct, delegate to struct instantiation
        if (resolution.HasStruct)
        {
            var fieldInits = call.Args.Select(a => (a.ParamName, a.Value)).ToList();
            return EvaluateStructCall(new Expression.StructCall(call.Name, fieldInits));
        }

        // find best overload
        if (resolution.HasUserOverloads == false)
        {
            throw new LangException($"Unknown function '{name}'", call.Name.Line, _filePath);
        }

        // evaluate args up front so we can match on types too
        var evaluatedArgs = new List<(Token ParamName, object? Value)>(call.Args.Count);
        foreach (var arg in call.Args)
        {
            evaluatedArgs.Add((arg.ParamName, Evaluate(arg.Value)));
        }

        var suppliedParamNames = call.SuppliedParamNames ??= new HashSet<string>(call.Args.Count, StringComparer.Ordinal);
        foreach (var arg in call.Args)
        {
            suppliedParamNames.Add(arg.ParamName.Lexeme);
        }

        var match = SelectBestNamedOverload(resolution.Candidates!, evaluatedArgs, suppliedParamNames);
        if (match == null)
        {
            throw new LangException($"Function '{name}' has no overload matching named parameters ({string.Join(", ", suppliedParamNames)})", call.Name.Line, _filePath);
        }

        // bind in parameter declaration order, filling defaults for omitted params
        var functionEnv = RentFunctionScope();
        var matchedParams = match.Params;
        for (int i = 0; i < matchedParams.Count; i++)
        {
            var param = matchedParams[i];
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
            else if (match.IsDefault[i])
            {
                functionEnv[param.Name.Lexeme] = Evaluate(param.Item3!);
            }
        }

        // check that all required parameters were bound
        for (int i = 0; i < matchedParams.Count; i++)
        {
            if (match.IsDefault[i] == false && functionEnv.ContainsKey(matchedParams[i].Name.Lexeme) == false)
            {
                ReturnFunctionScope(functionEnv);
                throw new LangException($"Missing required parameter '{matchedParams[i].Name.Lexeme}' when calling '{name}'", call.Name.Line, _filePath);
            }
        }

        return ExecuteFunctionBody(match, functionEnv);
    }

    private object? InvokeInternalNamedFunction(InternalFunctionDelegate internalFunc, string name, Expression.NamedCall call)
    {
        // get expected parameter names for this internal function
        var (paramNames, paramNameSet) = GetInternalFunctionParamNamesCached(name);

        // verify all supplied parameter names are valid
        foreach (var arg in call.Args)
        {
            if (paramNameSet.Contains(arg.ParamName.Lexeme) == false)
            {
                throw new LangException($"Function '{name}' has no parameter named '{arg.ParamName.Lexeme}'", call.Name.Line, _filePath);
            }
        }

        // verify all required parameters are supplied
        if (call.Args.Count != paramNames.Length)
        {
            throw new LangException($"Function '{name}' expects {paramNames.Length} argument(s), but got {call.Args.Count}", call.Name.Line, _filePath);
        }

        // reorder arguments to match the expected parameter order; the Args of a
        // NamedCall node are immutable, so the permutation can be cached on the node
        int[]? order = call.ArgOrder;
        if (order == null)
        {
            order = new int[paramNames.Length];
            for (int i = 0; i < paramNames.Length; i++)
            {
                int argIndex = -1;
                for (int j = 0; j < call.Args.Count; j++)
                {
                    if (call.Args[j].ParamName.Lexeme == paramNames[i])
                    {
                        argIndex = j;
                        break;
                    }
                }

                order[i] = argIndex;
            }

            call.ArgOrder = order;
        }

        var reorderedArgs = new List<Expression>(paramNames.Length);
        for (int i = 0; i < paramNames.Length; i++)
        {
            reorderedArgs.Add(call.Args[order[i]].Value);
        }

        // create a regular Call expression with reordered arguments
        var regularCall = new Expression.Call(new Expression.Variable(call.Name), reorderedArgs);
        return internalFunc(regularCall);
    }

    private UserFunctionInfo? SelectBestOverload(UserFunctionInfo[] overloads, IReadOnlyList<object?> argValues)
    {
        UserFunctionInfo? bestMatch = null;
        int bestRequired = -1;
        int bestAnyCount = int.MaxValue;

        foreach (var overload in overloads)
        {
            if (argValues.Count > overload.ParamCount)
            {
                continue;
            }

            int required = 0;
            bool matches = true;
            for (int i = 0; i < argValues.Count; i++)
            {
                if (overload.IsDefault[i] == false)
                {
                    required++;
                }

                if (overload.IsAny[i] == false && IsValueOfType(argValues[i], overload.TypeNames[i]) == false)
                {
                    matches = false;
                    break;
                }
            }

            for (int i = argValues.Count; i < overload.ParamCount; i++)
            {
                if (overload.IsDefault[i] == false)
                {
                    matches = false;
                    break;
                }
            }

            if (matches == false || argValues.Count < required)
            {
                continue;
            }

            if (required > bestRequired || (required == bestRequired && overload.AnyCount < bestAnyCount))
            {
                bestMatch = overload;
                bestRequired = required;
                bestAnyCount = overload.AnyCount;
            }
        }

        return bestMatch;
    }

    private UserFunctionInfo? SelectBestNamedOverload(
        UserFunctionInfo[] overloads,
        IReadOnlyList<(Token ParamName, object? Value)> evaluatedArgs,
        HashSet<string> suppliedParamNames)
    {
        UserFunctionInfo? bestMatch = null;
        int bestRequired = -1;
        int bestAnyCount = int.MaxValue;

        foreach (var overload in overloads)
        {
            int paramCount = overload.ParamCount;
            if (evaluatedArgs.Count > paramCount)
            {
                continue;
            }

            if (overload.ParamNameSet.IsSupersetOf(suppliedParamNames) == false)
            {
                continue;
            }

            bool valid = true;
            int required = 0;

            for (int i = 0; i < paramCount; i++)
            {
                if (overload.IsDefault[i] == false)
                {
                    required++;
                }

                bool found = false;
                for (int j = 0; j < evaluatedArgs.Count; j++)
                {
                    if (evaluatedArgs[j].ParamName.Lexeme == overload.Params[i].Name.Lexeme)
                    {
                        found = true;
                        if (overload.IsAny[i] == false && IsValueOfType(evaluatedArgs[j].Value, overload.TypeNames[i]) == false)
                        {
                            valid = false;
                            break;
                        }
                        break;
                    }
                }

                if (found == false && overload.IsDefault[i] == false)
                {
                    valid = false;
                    break;
                }
            }

            if (valid == false || evaluatedArgs.Count < required)
            {
                continue;
            }

            if (required > bestRequired || (required == bestRequired && overload.AnyCount < bestAnyCount))
            {
                bestMatch = overload;
                bestRequired = required;
                bestAnyCount = overload.AnyCount;
            }
        }

        return bestMatch;
    }

    private object? EvaluateStructCall(Expression.StructCall call)
    {
        string structName = call.Name.Lexeme;

        if (!_structs.TryGetValue(structName, out var defaults))
        {
            throw new LangException($"Unknown struct '{structName}'", call.Name.Line, _filePath);
        }

        // clone cached defaults directly
        var fields = new Dictionary<string, object?>(defaults);

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

        if (obj is StructInstance instance)
        {
            if (!instance.Fields.TryGetValue(m.Member.Lexeme, out var fieldValue))
            {
                throw new LangException($"Struct '{instance.TypeName}' has no member '{m.Member.Lexeme}'", m.Member.Line, _filePath);
            }

            return fieldValue;
        }

        if (obj is MapEntry entry)
        {
            return m.Member.Lexeme switch
            {
                "key"   => entry.Key,
                "value" => entry.Value,
                _ => throw new LangException($"MapEntry has no member '{m.Member.Lexeme}'", m.Member.Line, _filePath)
            };
        }

        throw new LangException($"Attempted to access member '{m.Member.Lexeme}' on a non-struct value (got '{GetValueType(obj)}')", m.Member.Line, _filePath);
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
        string lexeme = name.Lexeme;

        // iterate top-to-bottom using index access
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(lexeme, out var value))
            {
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

    private object? EvaluateMapIndex(Expression.MapIndex mi)
    {
        object? target = Evaluate(mi.Map);
        object? keyObj = Evaluate(mi.Key);

        if (target is Dictionary<object, object> map)
        {
            if (keyObj is null)
            {
                throw new LangException($"Cannot use 'nil' as map index key", mi.Bracket.Line, _filePath);
            }

            if (!map.TryGetValue(keyObj, out var value))
            {
                throw new LangException($"Map does not contain key '{Stringify(keyObj)}'", mi.Bracket.Line, _filePath);
            }

            return value;
        }

        if (target is List<object?> list)
        {
            if (keyObj is not double index)
            {
                throw new LangException($"List index must be a number, but got '{GetValueType(keyObj)}'", mi.Bracket.Line, _filePath);
            }

            int intIndex = (int)index;
            if (intIndex < 0 || intIndex >= list.Count)
            {
                throw new LangException($"List index {intIndex} out of range (list has {list.Count} elements)", mi.Bracket.Line, _filePath);
            }

            return list[intIndex];
        }

        throw new LangException($"Cannot index a '{GetValueType(target)}' with brackets", mi.Bracket.Line, _filePath);
    }

    private object? EvaluateListLiteral(Expression.ListLiteral ll)
    {
        var list = new List<object?>(ll.Elements.Count);

        foreach (var elementExpr in ll.Elements)
        {
            list.Add(Evaluate(elementExpr));
        }

        return list;
    }
}