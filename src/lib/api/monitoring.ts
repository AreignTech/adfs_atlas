export const monitoringPath = "/admin/api-management"
export interface DiagnosticRecord {
  id: string; kind: string; timestamp: string; requestId: string | null; correlationId: string | null;
  traceId: string | null; userId: string | null; method: string | null; endpoint: string | null;
  status: number; durationMs: number; isSlow: boolean; component: string | null; severity: string | null; payload: string;
}
export interface DiagnosticPage { records: DiagnosticRecord[]; total: number; page: number; pageSize: number }
export interface MetricBucket { label: string; total: number; failed: number; averageMs: number; p95Ms: number }
export interface Statistics {
  summary: { total: number; successful: number; failed: number; successPercentage: number; averageMs: number; p95Ms: number; authenticationFailures: number; rateLimited: number; slow: number };
  timeline: MetricBucket[]; endpoints: MetricBucket[]; slowestEndpoints: MetricBucket[]; statusCodes: MetricBucket[]; methods: MetricBucket[];
}
export interface MonitoringSettings {
  enabled: boolean; detailedErrors: boolean; logSuccessfulRequests: boolean; slowThresholdMs: number; retentionDays: number;
  captureRequestBody: boolean; captureResponseBody: boolean; maxRequestBodyBytes: number; maxResponseBodyBytes: number;
  sensitiveFields: string[]; excludedPrefixes: string[]; tracingEnabled: boolean; traceSampleRatio: number;
}
