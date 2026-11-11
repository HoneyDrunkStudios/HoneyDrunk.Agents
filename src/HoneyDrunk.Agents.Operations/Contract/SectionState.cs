namespace HoneyDrunk.Agents.Operations.Contract;

/// <summary>Whether a part of a response could be read from its source.</summary>
public enum SectionState
{
    /// <summary>The source answered; an empty value or list means there is nothing.</summary>
    Available = 0,

    /// <summary>The source is missing or failed; the section carries no value.</summary>
    Unavailable = 1,
}
