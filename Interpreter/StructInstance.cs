namespace JaskLang;

public class StructInstance
{
    public string TypeName { get; }

    // structs itself are immutable, hence IReadOnlyDictionary for fields
    public IReadOnlyDictionary<string, object?> Fields { get; }

    public StructInstance(string typeName, Dictionary<string, object?> fields)
    {
        TypeName = typeName;
        Fields = fields.AsReadOnly();
    }

    // returns a new StructInstance with one field replaced, leaving this instance unchanged
    public StructInstance WithField(string name, object? value)
    {
        var newFields = new Dictionary<string, object?>(Fields) { [name] = value };
        return new StructInstance(TypeName, newFields);
    }

    public override string ToString()
    {
        var fields = string.Join(", ", Fields.Select(kv => $"{kv.Key}: {Interpreter.Stringify(kv.Value)}"));
        return $"{TypeName} {{ {fields} }}";
    }

    public List<object?> getFieldNames()
    {
        List<object?> fieldNames = new List<object?>();
        foreach (var field in Fields)
        {
            fieldNames.Add(field.Key);
        }
        return fieldNames;
    }
}

public class MapEntry
{
    public object Key { get; }
    public object Value { get; }

    public MapEntry(object key, object value)
    {
        Key = key;
        Value = value;
    }

    public override string ToString() => $"MapEntry {{ key: {Interpreter.Stringify(Key)}, value: {Interpreter.Stringify(Value)} }}";
}