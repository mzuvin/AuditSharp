using System.Text.Json;
using AuditSharp.Core.Entities;
using AuditSharp.EntityFrameworkCore.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace AuditSharp.SqlServer.Extensions;

public class Interceptor : SaveChangesInterceptor
{
    private readonly List<TrackedChange> _trackedChanges = new();

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = new CancellationToken())
    {
        TrackChanges(eventData, result);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = new CancellationToken())
    {
        ProcessAsync(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ProcessAsync(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        TrackChanges(eventData, result);
        return base.SavedChanges(eventData, result);
    }

    private void ProcessAsync(DbContextEventData eventData)
    {
        var context = eventData.Context;

        _trackedChanges.Clear();

        foreach (var entry in context!.ChangeTracker.Entries().Where(e =>
                     e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted))
        {
            var oldValues = entry.State == EntityState.Added
                ? string.Empty
                : JsonSerializer.Serialize(
                    entry.OriginalValues.Properties.ToDictionary(p => p.Name, p => entry.GetDatabaseValues()?.GetValue<object>(p.Name)));

            _trackedChanges.Add(new TrackedChange(
                entry.Entity.GetType().Name,
                entry.State, oldValues,
                entry
            ));
        }
    }

    private int TrackChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (_trackedChanges.Count == 0) return result;

        var context = eventData.Context;
        if (context == null) return result;

        var auditLogs = new List<AuditLog>();

        foreach (var change in _trackedChanges)
        {
            var newValues = "";
            var primaryKeyProperties = change.Entry.Metadata.FindPrimaryKey()?.Properties;
            if (change.State != EntityState.Deleted)
            {
                var newValuesDict = change.Entry.CurrentValues.Properties.ToDictionary(p => p.Name, p => change.Entry.CurrentValues[p]);
                if (primaryKeyProperties != null)
                {
                    foreach (var pk in primaryKeyProperties)
                    {
                        newValuesDict[pk.Name] = change.Entry.CurrentValues[pk];
                    }
                }
                newValues = JsonSerializer.Serialize(newValuesDict);
            }
            var primaryKeyValue = primaryKeyProperties != null
                ? string.Join(",", primaryKeyProperties.Select(p =>
                    change.Entry.Property(p.Name).CurrentValue?.ToString() ?? "undefined"))
                : "undefined";
            auditLogs.Add(new AuditLog(
                change.EntityName,
                change.State.ToString(),
                change.OldValues,
                newValues,
                primaryKeyValue
            ));
        }

        var auditSharpContext = IoCManager.Instance.GetRequiredService<IAuditSharpContext>();
        foreach (var log in auditLogs)
        {
            auditSharpContext.InsertAsync(log);
        }

        _trackedChanges.Clear();
        return result;
    }

    private class TrackedChange(
        string entityName,
        EntityState state,
        string oldValues,
        EntityEntry entry)
    {
        public string EntityName { get; set; } = entityName;
        public EntityState State { get; set; } = state;
        public string OldValues { get; set; } = oldValues;
        public EntityEntry Entry { get; set; } = entry;
    }
}