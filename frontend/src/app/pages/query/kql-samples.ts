import { KqlSample } from '../../core/models';

/** Starting points shown in the "Sample queries" menu. */
export const KQL_SAMPLES: KqlSample[] = [
  {
    title: 'Failed requests by operation',
    description: 'Which endpoints fail most in the last 24 hours.',
    query: `requests
| where timestamp > ago(24h)
| where success == false
| summarize failures = count(), codes = make_set(resultCode, 5) by operation_Name
| top 20 by failures desc`,
  },
  {
    title: 'Slowest requests (P50 / P95 / P99)',
    description: 'Latency percentiles per operation, in milliseconds.',
    query: `requests
| where timestamp > ago(24h)
| summarize count(), p50 = percentile(duration, 50), p95 = percentile(duration, 95), p99 = percentile(duration, 99) by operation_Name
| top 20 by p95 desc`,
  },
  {
    title: 'Request rate and errors over time',
    description: 'Requests and failures in 15-minute buckets, as a chart-ready table.',
    query: `requests
| where timestamp > ago(24h)
| summarize total = count(), failed = countif(success == false) by bin(timestamp, 15m)
| order by timestamp asc`,
  },
  {
    title: 'Top exceptions',
    description: 'Most frequent exception types and messages.',
    query: `exceptions
| where timestamp > ago(7d)
| summarize occurrences = count(), lastSeen = max(timestamp), operations = dcount(operation_Id) by type, outerMessage
| top 20 by occurrences desc`,
  },
  {
    title: 'Slow or failing dependencies',
    description: 'SQL/HTTP calls by target, with failure rate and P95 duration.',
    query: `dependencies
| where timestamp > ago(24h)
| summarize calls = count(), failed = countif(success == false), p95 = percentile(duration, 95) by type, target
| extend failureRate = round(100.0 * failed / calls, 2)
| top 20 by p95 desc`,
  },
  {
    title: 'Errors and warnings in traces',
    description: 'Latest log messages with severity Warning or higher.',
    query: `traces
| where timestamp > ago(1h)
| where severityLevel >= 2
| project timestamp, severityLevel, message, cloud_RoleName, operation_Id
| order by timestamp desc
| take 200`,
  },
  {
    title: 'Everything for one operation',
    description: 'All telemetry for a single operation id (replace the id).',
    query: `union requests, dependencies, exceptions, traces
| where timestamp > ago(7d)
| where operation_Id == "<operation id>"
| project timestamp, itemType, name, message, type, outerMessage, resultCode, duration
| order by timestamp asc`,
  },
  {
    title: 'Custom property filter',
    description: 'Filter on a value in customDimensions (replace the key and value).',
    query: `traces
| where timestamp > ago(24h)
| where tostring(customDimensions["OrderId"]) == "4711"
| project timestamp, message, customDimensions
| take 100`,
  },
];

export const DEFAULT_QUERY = KQL_SAMPLES[0].query;
