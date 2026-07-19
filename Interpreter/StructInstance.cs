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
}