namespace EfCore.MultiTenancy.Core.Exceptions;

/// <summary>Thrown when a schema name fails validation before being used in SQL.</summary>
public class InvalidSchemaNameException : Exception
{
    public string SchemaName { get; }

    public InvalidSchemaNameException(string schemaName, string reason)
        : base($"Invalid schema name '{schemaName}': {reason}")
    {
        SchemaName = schemaName;
    }
}
