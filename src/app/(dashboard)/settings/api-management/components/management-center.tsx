"use client"

import { useCallback, useEffect, useState } from "react"
import dynamic from "next/dynamic"
import Link from "next/link"
import { api } from "@/lib/api/client"
import { monitoringPath, type DiagnosticPage, type DiagnosticRecord, type Statistics } from "@/lib/api/monitoring"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Badge } from "@/components/ui/badge"
import { ConfigurationForm } from "./monitoring-configuration"
import { HealthCharts } from "./monitoring-charts"

const ScalarReference = dynamic(() => import("./scalar-reference"), { ssr: false, loading: () => <p>Loading API explorer…</p> })
const sections = [ ["health", "API Health and Statistics"], ["documentation", "API Documentation"], ["activity", "API Activity Logs"], ["diagnostics", "API Diagnostics"], ["configuration", "API Configuration"] ]
const selectClass = "bg-background h-9 w-full rounded-md border px-3 text-sm"
const localDate = (value: string) => new Date(value).toLocaleString()
const ms = (value: number) => `${value.toFixed(1)} ms`

export function ManagementCenter({ section }: { section: string }) {
  return (
    <div className="flex min-w-0 flex-col gap-6 px-4 lg:px-6">
      <div><h1 className="text-3xl font-bold">{sections.find(([key]) => key === section)?.[1]}</h1>
        <p className="text-muted-foreground">API Management · Documentation, activity and system diagnostics</p></div>
      <nav aria-label="API Management" className="flex flex-wrap gap-2">
        {sections.map(([key, label]) => <Link key={key} href={`/settings/api-management${key === "health" ? "" : `/${key}`}`} aria-current={key === section ? "page" : undefined}
          className={`rounded-md border px-3 py-2 text-sm ${key === section ? "bg-primary text-primary-foreground" : "hover:bg-muted"}`}>{label.replace("API ", "")}</Link>)}
      </nav>
      {section === "documentation" ? <>
        <p className="text-sm text-muted-foreground">Test requests run as your signed-in account and can change application data. Session cookies and CSRF tokens are supplied automatically.</p>
        <div className="flex flex-wrap gap-4 text-sm underline underline-offset-4"><a href="/api/atlas/admin/api-management/openapi/v1.json" download="atlas-openapi.json">Download OpenAPI JSON</a><a href="/api/atlas/admin/api-management/openapi/v1.yaml" download="atlas-openapi.yaml">Download OpenAPI YAML</a></div>
        <ScalarReference />
      </> : section === "configuration" ? <ConfigurationForm /> : <ActivityDashboard section={section} />}
    </div>
  )
}

function ActivityDashboard({ section }: { section: string }) {
  const [period, setPeriod] = useState("1440")
  const [customFrom, setCustomFrom] = useState("")
  const [customTo, setCustomTo] = useState("")
  const [filters, setFilters] = useState({ query: "", userId: "", method: "", endpoint: "", status: "", result: section === "diagnostics" ? "failed" : "", kind: "activity", sort: "timestamp", descending: "true" })
  const [page, setPage] = useState(1)
  const [data, setData] = useState<DiagnosticPage | null>(null)
  const [statistics, setStatistics] = useState<Statistics | null>(null)
  const [ingestion, setIngestion] = useState<Record<string, unknown> | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [detail, setDetail] = useState<DiagnosticRecord | null>(null)
  const [related, setRelated] = useState<DiagnosticRecord[]>([])
  const [exportQuery, setExportQuery] = useState("")
  const [refresh, setRefresh] = useState(0)

  const load = useCallback(async (signal: AbortSignal) => {
    const to = period === "custom" ? new Date(customTo) : new Date()
    const from = period === "custom" ? new Date(customFrom) : new Date(to.getTime() - Number(period) * 60000)
    if (!Number.isFinite(from.getTime()) || !Number.isFinite(to.getTime()) || from > to || to.getTime() - from.getTime() > 31 * 86400000) { setError("Choose a valid reporting range of at most 31 days."); setLoading(false); return }
    const range = new URLSearchParams({ from: from.toISOString(), to: to.toISOString() })
    const query = new URLSearchParams(range)
    for (const [key, value] of Object.entries(filters)) if (value) query.set(key, value)
    query.set("page", String(page)); query.set("pageSize", section === "health" ? "10" : "25")
    setExportQuery(query.toString())
    try {
      const [records, stats, health] = await Promise.all([
        api<DiagnosticPage>(`${monitoringPath}/activity?${query}`, "GET", undefined, undefined, signal),
        section === "health" ? api<Statistics>(`${monitoringPath}/statistics?${range}`, "GET", undefined, undefined, signal) : Promise.resolve(null),
        api<Record<string, unknown>>(`${monitoringPath}/ingestion`, "GET", undefined, undefined, signal),
      ])
      if (!signal.aborted) { setData(records); setStatistics(stats); setIngestion(health); setError(null) }
    } catch (failure) { if (!signal.aborted) setError(failure instanceof Error ? failure.message : "Could not load diagnostics.") }
    finally { if (!signal.aborted) setLoading(false) }
  }, [period, customFrom, customTo, filters, page, section])
  useEffect(() => { const controller = new AbortController(); let running = false; const run = async () => { if (running) return; running = true; try { await load(controller.signal) } finally { running = false } }; void run(); const timer = window.setInterval(() => void run(), 15000); return () => { controller.abort(); clearInterval(timer) } }, [load, refresh])

  async function open(record: DiagnosticRecord) {
    try {
      const entry = await api<DiagnosticRecord>(`${monitoringPath}/activity/${record.id}`)
      setDetail(entry)
      if (entry.correlationId) {
        const query = new URLSearchParams({ kind: "application", correlationId: entry.correlationId, from: new Date(new Date(entry.timestamp).getTime() - 60000).toISOString(), to: new Date(new Date(entry.timestamp).getTime() + 60000).toISOString() })
        const logs = await api<DiagnosticPage>(`${monitoringPath}/activity?${query}`)
        query.set("kind", "trace")
        const traces = await api<DiagnosticPage>(`${monitoringPath}/activity?${query}`)
        setRelated([...logs.records, ...traces.records])
      } else setRelated([])
    } catch (failure) { setError(failure instanceof Error ? failure.message : "Could not open record.") }
  }
  return <>
    <Card><CardContent className="flex flex-wrap items-end gap-3 pt-6">
      <label className="grid gap-1 text-sm">Reporting period<select className={selectClass} value={period} onChange={event => { setPeriod(event.target.value); setPage(1) }}>
        {[["15", "Last 15 minutes"], ["60", "Last hour"], ["1440", "Last 24 hours"], ["10080", "Last 7 days"], ["43200", "Last 30 days"], ["custom", "Custom range"]].map(([value, label]) => <option key={value} value={value}>{label}</option>)}
      </select></label>
      {period === "custom" && <><label className="grid gap-1 text-sm">From<Input type="datetime-local" value={customFrom} onChange={event => setCustomFrom(event.target.value)} /></label><label className="grid gap-1 text-sm">To<Input type="datetime-local" value={customTo} onChange={event => setCustomTo(event.target.value)} /></label></>}
      <Button variant="outline" onClick={() => { setLoading(true); setRefresh(value => value + 1) }}>Refresh</Button>
      <span className="text-muted-foreground text-xs">Refreshes every 15 seconds. Ingestion is asynchronous.</span>
    </CardContent></Card>
    {error && <p role="alert" className="text-destructive">{error}</p>}
    {loading && <p role="status">Loading API activity…</p>}
    {statistics && <>
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {[["Total API Requests", statistics.summary.total.toLocaleString()], ["Successful Requests", `${statistics.summary.successPercentage.toFixed(1)}%`], ["Failed Requests", statistics.summary.failed.toLocaleString()], ["Average Response Time", ms(statistics.summary.averageMs)]].map(([label, value]) => <Card key={label}><CardHeader><CardTitle className="text-sm text-muted-foreground">{label}</CardTitle></CardHeader><CardContent className="text-3xl font-bold">{value}</CardContent></Card>)}
      </div>
      <div className="flex flex-wrap gap-4 rounded-lg border p-4 text-sm">
        <span>P95: {ms(statistics.summary.p95Ms)}</span><span>Successful: {statistics.summary.successful}</span><span>Error rate: {statistics.summary.total ? (statistics.summary.failed / statistics.summary.total * 100).toFixed(1) : "0"}%</span>
        <span>Authentication failures: {statistics.summary.authenticationFailures}</span><span>Rate limited: {statistics.summary.rateLimited}</span><span>Slow: {statistics.summary.slow}</span>
      </div>
    </>}
    {section !== "health" && <Card><CardHeader><CardTitle>Search and filters</CardTitle></CardHeader><CardContent className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      <label className="grid gap-1 text-sm">Log stream<select className={selectClass} value={filters.kind} onChange={event => { setFilters({ ...filters, kind: event.target.value, result: "" }); setPage(1) }}><option value="activity">API activity</option><option value="application">Application events</option><option value="trace">Trace spans</option></select></label>
      {([ ["query", "Search / request or correlation ID"], ["userId", "User ID"], ["endpoint", "Endpoint contains"] ] as const).map(([key, label]) => <label key={key} className="grid gap-1 text-sm">{label}<Input value={filters[key]} onChange={event => { setFilters({ ...filters, [key]: event.target.value }); setPage(1) }} /></label>)}
      <label className="grid gap-1 text-sm">Method<select className={selectClass} value={filters.method} onChange={event => { setFilters({ ...filters, method: event.target.value }); setPage(1) }}>{["", "GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD"].map(value => <option key={value} value={value}>{value || "All methods"}</option>)}</select></label>
      <label className="grid gap-1 text-sm">HTTP status<Input type="number" min="100" max="599" value={filters.status} onChange={event => { setFilters({ ...filters, status: event.target.value }); setPage(1) }} /></label>
      <label className="grid gap-1 text-sm">Result<select className={selectClass} value={filters.result} onChange={event => { setFilters({ ...filters, result: event.target.value }); setPage(1) }}>{[["", "All results"], ["success", "Successful"], ["failed", "Failed"], ["slow", "Slow"], ["authentication", "Authentication failure"], ["authorization", "Authorization failure"]].map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
      <label className="grid gap-1 text-sm">Sort<select className={selectClass} value={filters.sort} onChange={event => setFilters({ ...filters, sort: event.target.value })}>{[["timestamp", "Timestamp"], ["duration", "Duration"], ["status", "Status"], ["method", "Method"], ["endpoint", "Endpoint"]].map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label>
      <label className="grid gap-1 text-sm">Order<select className={selectClass} value={filters.descending} onChange={event => setFilters({ ...filters, descending: event.target.value })}><option value="true">Descending</option><option value="false">Ascending</option></select></label>
    </CardContent></Card>}
    <Card><CardHeader className="flex flex-wrap flex-row items-center justify-between gap-3"><CardTitle>{section === "health" ? "Recent API Activity" : "Diagnostic records"}</CardTitle>
      {section === "health" ? <Link className="text-sm underline" href="/settings/api-management/activity">View all activity</Link> : <div className="flex flex-wrap gap-3 text-sm underline"><a href={`/api/atlas${monitoringPath}/export?${exportQuery}&format=csv`}>Export CSV</a><a href={`/api/atlas${monitoringPath}/export?${exportQuery}&format=json`}>Export JSON</a></div>}
    </CardHeader><CardContent>
      <div className="overflow-x-auto"><table className="w-full text-left text-sm"><thead><tr className="border-b">{["Timestamp / request", "User / IP", "Method", "Endpoint / component", "Status", "Duration", "Result"].map(label => <th className="whitespace-nowrap px-3 py-3 font-medium" key={label}>{label}</th>)}</tr></thead>
        <tbody>{data?.records.map(record => {
          const payload = JSON.parse(record.payload) as Record<string, unknown>
          return <tr key={record.id} className="border-b hover:bg-muted/50"><td className="px-3 py-3"><button className="cursor-pointer text-left underline underline-offset-4" onClick={() => void open(record)}>{localDate(record.timestamp)}</button><div className="max-w-44 truncate font-mono text-xs" title={record.requestId ?? ""}>{record.requestId}</div><div className="max-w-44 truncate font-mono text-xs text-muted-foreground" title={record.correlationId ?? ""}>{record.correlationId}</div></td>
            <td className="max-w-40 truncate px-3 py-3" title={record.userId ?? ""}>{record.userId ?? "Anonymous / system"}<div className="text-xs text-muted-foreground">{String(payload.ipAddress ?? "")}</div></td>
            <td className="px-3 py-3 font-mono">{record.method ?? record.kind}</td><td className="max-w-80 break-all px-3 py-3 font-mono text-xs">{record.endpoint ?? record.component}</td><td className="px-3 py-3"><Badge variant={record.status >= 400 ? "destructive" : "secondary"}>{record.status || record.severity}</Badge></td><td className="whitespace-nowrap px-3 py-3">{ms(record.durationMs)}</td><td className="px-3 py-3">{record.isSlow ? "Slow" : record.status === 401 ? "Authentication failure" : record.status === 403 ? "Access denied" : record.status >= 400 ? "Failed" : record.kind === "application" ? record.severity : "Success"}</td></tr>
        })}</tbody></table></div>
      {!loading && !data?.records.length && <p className="py-8 text-center text-muted-foreground">No records match this reporting period and filters.</p>}
      <div className="mt-4 flex flex-wrap items-center justify-between gap-3 text-sm"><span>{data?.total ?? 0} records · Page {page}{section !== "health" && " · Exports include up to 1,000 matching records"}</span><div className="flex gap-2"><Button variant="outline" disabled={page <= 1} onClick={() => setPage(value => value - 1)}>Previous</Button><Button variant="outline" disabled={!data || page * data.pageSize >= data.total} onClick={() => setPage(value => value + 1)}>Next</Button></div></div>
    </CardContent></Card>
    {detail && <RequestDetails record={detail} related={related} onClose={() => setDetail(null)} />}
    {statistics && (statistics.summary.total > 0 ? <HealthCharts statistics={statistics} /> : <p className="rounded-lg border p-6 text-muted-foreground">Statistics will appear once API requests have been recorded during this period.</p>)}
    {ingestion && <p className="text-muted-foreground text-xs">Ingestion: {String(ingestion.queued)} queued · {String(ingestion.written)} written · {String(ingestion.dropped)} dropped · {String(ingestion.failures)} storage failures. Logs are best effort and are not a durable audit queue.</p>}
  </>
}

function RequestDetails({ record, related, onClose }: { record: DiagnosticRecord; related: DiagnosticRecord[]; onClose: () => void }) {
  const [copied, setCopied] = useState("")
  const payload = JSON.parse(record.payload) as Record<string, unknown>
  async function copy(label: string, value: string) { try { await navigator.clipboard.writeText(value); setCopied(`${label} copied.`) } catch { setCopied("Copy unavailable. Select the ID below.") } }
  return <Card id="request-details"><CardHeader className="flex flex-row justify-between"><CardTitle><h2>Request details</h2></CardTitle><Button variant="ghost" onClick={onClose}>Close details</Button></CardHeader><CardContent className="grid min-w-0 gap-5">
    <div className="flex flex-wrap gap-2"><Button variant="outline" disabled={!record.requestId} onClick={() => void copy("Request ID", record.requestId!)}>Copy Request ID</Button><Button variant="outline" disabled={!record.correlationId} onClick={() => void copy("Correlation ID", record.correlationId!)}>Copy Correlation ID</Button><span role="status" className="text-sm">{copied}</span></div>
    <section><h3 className="mb-2 font-semibold">Request</h3><pre className="overflow-auto rounded-md bg-muted p-4 text-xs">{JSON.stringify({ requestId: record.requestId, correlationId: record.correlationId, timestamp: record.timestamp, method: record.method, endpoint: record.endpoint, userId: record.userId, authenticated: payload.authenticated, ipAddress: payload.ipAddress, userAgent: payload.userAgent, query: payload.query, headers: payload.requestHeaders, body: payload.requestBody ?? "Body capture omitted or disabled" }, null, 2)}</pre></section>
    <section><h3 className="mb-2 font-semibold">Response</h3><pre className="overflow-auto rounded-md bg-muted p-4 text-xs">{JSON.stringify({ status: record.status, durationMs: record.durationMs, bytes: payload.responseSize, headers: payload.responseHeaders, body: payload.responseBody ?? "Body capture omitted or disabled", error: payload.error }, null, 2)}</pre></section>
    <section><h3 className="mb-2 font-semibold">Diagnostics and related application events / trace spans</h3><pre className="max-h-96 overflow-auto rounded-md bg-muted p-4 text-xs">{JSON.stringify({ traceId: record.traceId, component: record.component, exceptionType: payload.exceptionType, stack: payload.stack, related: related.map(item => ({ ...item, payload: JSON.parse(item.payload) })) }, null, 2)}</pre><p className="mt-2 text-xs text-muted-foreground">SQL, external calls and internal spans appear when tracing is enabled and the request is sampled. Application event argument values are omitted.</p></section>
  </CardContent></Card>
}
