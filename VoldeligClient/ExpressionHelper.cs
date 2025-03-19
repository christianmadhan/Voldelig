using System.Linq.Expressions;
using System.Text.RegularExpressions;

public static class ExpressionHelper
{
    public static string ReplaceEnumValues<T>(string expression)
    {
        var enumPropertyMap = new Dictionary<string, Type>();
        foreach (var property in typeof(T).GetProperties())
        {
            if (property.PropertyType.IsEnum)
            {
                enumPropertyMap.Add(property.Name, property.PropertyType);
            }
        }

        var enumTypes = enumPropertyMap.ToDictionary(
            x => x.Value.Name,
            x => Enum.GetValues(x.Value)
                    .Cast<Enum>()
                    .ToDictionary(e => (int)(object)e, e => e.ToString()));

        // Updated regex to handle both = and != operators
        var matches = Regex.Matches(expression, @"(\w+)\s*(!?=)\s*(\d+)");

        foreach (Match match in matches)
        {
            var propertyName = match.Groups[1].Value;
            var operator_ = match.Groups[2].Value;  // This will capture either "=" or "!="
            var enumValue = int.Parse(match.Groups[3].Value);

            if (enumPropertyMap.ContainsKey(propertyName))
            {
                var enumType = enumPropertyMap[propertyName].Name;
                var replacement = enumTypes[enumType][enumValue];
                expression = expression.Replace(
                    $"{propertyName} {operator_} {enumValue}",
                    $"{propertyName} {operator_} {enumType}'{replacement.ToLower()}"
                );
            }
        }

        return expression;
    }

    private static Dictionary<int, string> GetEnumValues<T>() where T : Enum
    {
        return Enum.GetValues(typeof(T))
          .Cast<T>()
          .ToDictionary(e => (int)(object)e, e => e.ToString());
    }

    public static string ExpressionToFilterString<T>(Expression<Func<T, bool>> expression)
    {
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));

        string filterString = ParseExpression<T>(expression.Body);
        filterString = ReplaceEnumValues<T>(filterString);
        return filterString;

    }

    private static string ParseExpression<T>(Expression expression)
    {
        switch (expression)
        {
            case BinaryExpression binary:
                return HandleBinaryExpression<T>(binary);

            case MemberExpression member:
                return HandleMemberExpression<T>(member);

            case ConstantExpression constant:
                return FormatConstant(constant.Value);

            case UnaryExpression unary:
                return HandleUnaryExpression<T>(unary);

            case MethodCallExpression methodCall:
                return HandleMethodCall<T>(methodCall);

            case NewExpression newExpr:
                return HandleNewExpression<T>(newExpr);

            default:
                return $"Unsupported_{expression.NodeType}";
        }
    }

    private static string HandleBinaryExpression<T>(BinaryExpression binary)
    {
        string left = ParseExpression<T>(binary.Left);
        string right = ParseExpression<T>(binary.Right);
        string op = GetOperator(binary.NodeType);
        // Check if the left side is an enum property
        if (binary.Left is MemberExpression memberExpr)
        {
            // Check if the right side is an integer constant
            if (binary.Right is ConstantExpression constant && constant.Value is int intValue)
            {
                // Try to map the integer value to the corresponding enum value
                string enumName = Enum.GetName(memberExpr.Type, intValue);
                if (enumName != null)
                {
                    // Convert the integer to the actual enum and pass it to FormatConstant
                    right = FormatConstant(Enum.Parse(memberExpr.Type, enumName));
                }
                else
                {
                    // Handle the case where the integer doesn't map to a valid enum value
                    right = intValue.ToString();
                }
            }
        }
        return $"{left} {op} {right}";
    }

    private static string HandleUnaryExpression<T>(UnaryExpression unary)
    {
        if (unary.NodeType == ExpressionType.Not)
        {
            return $"!({ParseExpression<T>(unary.Operand)})";
        }

        if (unary.NodeType == ExpressionType.Convert)
        {
            // Check if the operand is an enum
            if (unary.Operand.Type.IsEnum)
            {
                return ParseExpression<T>(unary.Operand);
            }
            // Special handling for enum conversion
            if (unary.Operand is MemberExpression memberExpr && memberExpr.Member.DeclaringType.IsEnum)
            {
                return HandleEnumMember(memberExpr);
            }

            return ParseExpression<T>(unary.Operand);
        }

        return $"Unsupported_Unary_{unary.NodeType}";
    }

    private static string HandleMemberExpression<T>(MemberExpression member)
    {

        if (member.Member.DeclaringType == typeof(T))
        {
            return member.Member.Name;
        }
        else
        {
            // Handle nested properties
            return $"{ParseExpression<T>(member.Expression)}.{member.Member.Name}";
        }
    }

    private static string HandleEnumMember(MemberExpression member)
    {
        try
        {
            // Try to evaluate the enum member
            var enumValue = Expression.Lambda(member).Compile().DynamicInvoke();
            if (enumValue is Enum value)
            {
                return FormatEnum(value);
            }
        }
        catch { /* Ignore evaluation errors */ }

        // If we can't evaluate it directly, try to get the enum name from the member expression
        string enumTypeName = member.Type.Name;
        string enumValueName = member.Member.Name;
        return $"{enumTypeName}'{enumValueName.ToLower()}";
    }

    private static string HandleMethodCall<T>(MethodCallExpression methodCall)
    {
        string methodName = methodCall.Method.Name;

        if (methodCall.Object != null && methodCall.Object.Type == typeof(string))
        {
            string propertyName = ParseExpression<T>(methodCall.Object);

            switch (methodName)
            {
                case "Contains" when methodCall.Arguments.Count == 1:
                    string value = ParseExpression<T>(methodCall.Arguments[0]).Trim('"'); // Remove extra quotes
                    return $"{propertyName} like \"*{value}*\"";

                case "StartsWith" when methodCall.Arguments.Count == 1:
                    value = ParseExpression<T>(methodCall.Arguments[0]).Trim('"'); // Remove extra quotes
                    return $"{propertyName} like \"{value}*\"";

                case "EndsWith" when methodCall.Arguments.Count == 1:
                    value = ParseExpression<T>(methodCall.Arguments[0]).Trim('"'); // Remove extra quotes
                    return $"{propertyName} like \"*{value}\"";
            }
        }


        // Try to evaluate the method call if it's a constant
        try
        {
            var methodCallResult = Expression.Lambda(methodCall).Compile().DynamicInvoke();
            if (methodCallResult is DateTime dateTime)
            {
                return $"date({dateTime.Year},{dateTime.Month:00},{dateTime.Day:00})";
            }

            if (methodCallResult is Enum enumValue)
            {
                return FormatEnum(enumValue);
            }

            return FormatConstant(methodCallResult);
        }
        catch
        {
            // If we can't evaluate, return a placeholder
            return $"method_{methodName}";
        }
    }

    private static string HandleNewExpression<T>(NewExpression newExpr)
    {
        // Handle DateTime constructor
        if (newExpr.Constructor.DeclaringType == typeof(DateTime))
        {
            // Try to evaluate the constructor arguments
            var args = newExpr.Arguments.Select(arg => {
                try
                {
                    return Expression.Lambda(arg).Compile().DynamicInvoke();
                }
                catch
                {
                    return null;
                }
            }).ToArray();

            // If all args are constants, we can format the date
            if (args.All(a => a != null) && args.Length >= 3)
            {
                return $"date({args[0]},{args[1]:00},{args[2]:00})";
            }

            // Otherwise try to evaluate the entire expression
            try
            {
                var dateTime = (DateTime)Expression.Lambda(newExpr).Compile().DynamicInvoke();
                return $"date({dateTime.Year},{dateTime.Month:00},{dateTime.Day:00})";
            }
            catch
            {
                // If we can't evaluate, return a placeholder with literal arguments
                var argStrings = newExpr.Arguments.Select(ParseExpression<T>).ToArray();
                return $"date({string.Join(",", argStrings)})";
            }
        }

        return $"Unsupported_Constructor";
    }

    private static string GetOperator(ExpressionType nodeType)
    {
        return nodeType switch
        {
            ExpressionType.Equal => "=",
            ExpressionType.NotEqual => "!=",
            ExpressionType.GreaterThan => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            ExpressionType.LessThan => "<",
            ExpressionType.LessThanOrEqual => "<=",
            ExpressionType.AndAlso => "and",
            ExpressionType.OrElse => "or",
            ExpressionType.Add => "+",
            ExpressionType.Subtract => "-",
            ExpressionType.Multiply => "*",
            ExpressionType.Divide => "/",
            _ => $"_Unsupported_{nodeType}_"
        };
    }

    private static string FormatConstant(object value)
    {
        if (value == null)
            return "null";

        if (value is string str)
            return $"\"{str}\"";

        if (value is DateTime dt)
            return $"date({dt.Year},{dt.Month:00},{dt.Day:00})";

        if (value is bool b)
            return b.ToString().ToLower();

        if (value is Enum enumValue)
            return FormatEnum(enumValue);

        return value.ToString();
    }

    private static string FormatEnum(Enum enumValue)
    {
        string enumTypeName = enumValue.GetType().Name;
        string enumValueName = enumValue.ToString();
        return $"{enumTypeName}'{enumValueName.ToLower()}";
    }
}