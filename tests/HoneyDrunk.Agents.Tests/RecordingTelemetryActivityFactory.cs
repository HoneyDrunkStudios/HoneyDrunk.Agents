using HoneyDrunk.Kernel.Abstractions.Context;
using HoneyDrunk.Kernel.Abstractions.Telemetry;
using System.Diagnostics;

namespace HoneyDrunk.Agents.Tests;

/// <summary>Starts real activities and keeps them, so tests can read every tag that was set.</summary>
internal sealed class RecordingTelemetryActivityFactory : ITelemetryActivityFactory
{
    private readonly List<Activity> activities = [];

    public IReadOnlyList<Activity> Activities => this.activities;

    /// <summary>Gets every tag set on any recorded activity.</summary>
    public IReadOnlyList<KeyValuePair<string, object?>> Tags => [.. this.activities.SelectMany(activity => activity.TagObjects)];

    public Activity? Start(string name, IReadOnlyDictionary<string, object?>? additionalTags = null)
    {
        var activity = new Activity(name);
        foreach (var tag in additionalTags ?? new Dictionary<string, object?>())
        {
            activity.SetTag(tag.Key, tag.Value);
        }

        activity.Start();
        this.activities.Add(activity);
        return activity;
    }

    public Activity? StartExplicit(string name, IGridContext gridContext, IOperationContext? operationContext = null, IReadOnlyDictionary<string, object?>? additionalTags = null) =>
        this.Start(name, additionalTags);
}
