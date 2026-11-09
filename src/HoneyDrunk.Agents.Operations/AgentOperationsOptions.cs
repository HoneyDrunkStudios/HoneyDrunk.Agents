namespace HoneyDrunk.Agents.Operations;

/// <summary>Configures the operations endpoints of one agent host.</summary>
public sealed class AgentOperationsOptions
{
    /// <summary>
    /// Gets or sets the environment this host runs in, as every workload reports it. Required: 1 to 64 characters of
    /// a-z, 0-9, '.', '_' or '-', starting with a letter or digit.
    /// </summary>
    public string Environment { get; set; } = string.Empty;
}
