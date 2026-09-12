using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UnityEventAuditor.Editor
{
    /// <summary>
    /// Serializes UnityEvent audit scan results into CSV or JSON reports for QA and CI pipelines.
    /// </summary>
    public static class UnityEventAuditExporter
    {
        [Serializable]
        private sealed class AuditReportDto
        {
            public int totalEvents;
            public int totalListeners;
            public int brokenListeners;
            public int emptyEvents;
            public List<EventEntryDto> events = new();
        }

        [Serializable]
        private sealed class EventEntryDto
        {
            public string ownerName;
            public string componentType;
            public string propertyPath;
            public string assetPath;
            public int listenerCount;
            public int brokenCount;
            public List<ListenerDto> listeners = new();
        }

        [Serializable]
        private sealed class ListenerDto
        {
            public string targetName;
            public string methodName;
            public string mode;
            public bool isBroken;
            public string issue;
            public string reason;
        }

        public static string ToJson(IReadOnlyList<UnityEventScanner.EventEntry> entries, bool prettyPrint = true)
        {
            var summary = UnityEventScanner.ComputeSummary(entries);
            var report = new AuditReportDto
            {
                totalEvents = summary.TotalEvents,
                totalListeners = summary.TotalListeners,
                brokenListeners = summary.BrokenListeners,
                emptyEvents = summary.EmptyEvents
            };

            if (entries != null)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry == null) continue;

                    var dto = new EventEntryDto
                    {
                        ownerName = entry.OwnerName ?? string.Empty,
                        componentType = entry.Component ? entry.Component.GetType().Name : (entry.OwnerObject ? entry.OwnerObject.GetType().Name : string.Empty),
                        propertyPath = !string.IsNullOrEmpty(entry.PropertyPath) ? entry.PropertyPath : entry.FieldName,
                        assetPath = entry.AssetPath ?? string.Empty,
                        listenerCount = entry.Listeners.Count,
                        brokenCount = entry.BrokenListenerCount
                    };

                    for (var j = 0; j < entry.Listeners.Count; j++)
                    {
                        var listener = entry.Listeners[j];
                        dto.listeners.Add(new ListenerDto
                        {
                            targetName = listener.Target ? listener.Target.name : "NULL",
                            methodName = listener.MethodName ?? string.Empty,
                            mode = listener.Mode.ToString(),
                            isBroken = listener.IsBroken,
                            issue = listener.Issue.ToString(),
                            reason = listener.BrokenReason
                        });
                    }

                    report.events.Add(dto);
                }
            }

            return JsonUtility.ToJson(report, prettyPrint);
        }

        public static string ToCsv(IReadOnlyList<UnityEventScanner.EventEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Owner,Component,PropertyPath,AssetPath,ListenerTarget,MethodName,Status,Issue,Reason");

            if (entries == null)
            {
                return sb.ToString();
            }

            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;

                var owner = EscapeCsv(entry.OwnerName);
                var component = EscapeCsv(entry.Component ? entry.Component.GetType().Name : (entry.OwnerObject ? entry.OwnerObject.GetType().Name : string.Empty));
                var propPath = EscapeCsv(!string.IsNullOrEmpty(entry.PropertyPath) ? entry.PropertyPath : entry.FieldName);
                var assetPath = EscapeCsv(entry.AssetPath);

                if (entry.Listeners.Count == 0)
                {
                    sb.AppendLine($"{owner},{component},{propPath},{assetPath},,,Empty,None,No listeners attached");
                    continue;
                }

                for (var j = 0; j < entry.Listeners.Count; j++)
                {
                    var listener = entry.Listeners[j];
                    var target = EscapeCsv(listener.Target ? listener.Target.name : "NULL");
                    var method = EscapeCsv(listener.MethodName);
                    var status = listener.IsBroken ? "Broken" : "Valid";
                    var issue = listener.Issue.ToString();
                    var reason = EscapeCsv(listener.BrokenReason);
                    sb.AppendLine($"{owner},{component},{propPath},{assetPath},{target},{method},{status},{issue},{reason}");
                }
            }

            return sb.ToString();
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }
    }
}
