"use client"

import { useEffect, useState } from "react"
import { api } from "@/lib/api/client"
import { monitoringPath, type MonitoringSettings } from "@/lib/api/monitoring"
import { Card, CardHeader, CardTitle, CardContent } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Button } from "@/components/ui/button"

export function ConfigurationForm() {
  const [settings, setSettings] = useState<MonitoringSettings | null>(null)
  const [error, setError] = useState("")
  const [message, setMessage] = useState("")
  const [pending, setPending] = useState(false)
  useEffect(() => { api<MonitoringSettings>(`${monitoringPath}/configuration`).then(setSettings).catch(failure => setError(failure instanceof Error ? failure.message : "Could not load settings.")) }, [])
  async function save(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!settings || pending) return
    setPending(true); setError(""); setMessage("")
    try { setSettings(await api<MonitoringSettings>(`${monitoringPath}/configuration`, "PUT", settings)); setMessage("Monitoring configuration saved and audited.") }
    catch (failure) { setError(failure instanceof Error ? failure.message : "Could not save configuration.") }
    finally { setPending(false) }
  }
  return <Card><CardHeader><CardTitle>Logging and tracing settings</CardTitle></CardHeader><CardContent>
    {error && <p role="alert" className="mb-4 text-destructive">{error}</p>}
    {message && <p role="status" className="mb-4">{message}</p>}
    {!settings ? <p>Loading monitoring configuration…</p> : <form onSubmit={save} className="grid gap-6">
      <div className="grid gap-4 sm:grid-cols-2">{([
        ["enabled", "Enable persistent API and application logging"], ["detailedErrors", "Include exception types and sanitized stack frames for failed requests"],
        ["logSuccessfulRequests", "Log successful requests"], ["captureRequestBody", "Capture sanitized request bodies"], ["captureResponseBody", "Capture sanitized response bodies"], ["tracingEnabled", "Enable OpenTelemetry tracing"],
      ] as const).map(([key, label]) => <label key={key} className="flex items-start gap-3 text-sm"><input type="checkbox" className="mt-1" checked={settings[key]} onChange={event => setSettings({ ...settings, [key]: event.target.checked })} />{label}</label>)}</div>
      <div className="grid gap-4 sm:grid-cols-2">{([
        ["slowThresholdMs", "Slow-request threshold (ms)", 1, 300000, 1], ["retentionDays", "Retention (days)", 1, 365, 1],
        ["maxRequestBodyBytes", "Maximum request capture (bytes)", 0, 16384, 1], ["maxResponseBodyBytes", "Maximum response capture (bytes)", 0, 16384, 1], ["traceSampleRatio", "Trace sample ratio (0–1)", 0, 1, 0.01],
      ] as const).map(([key, label, min, max, step]) => <label key={key} className="grid gap-2 text-sm">{label}<Input type="number" required min={min} max={max} step={step} value={settings[key]} onChange={event => setSettings({ ...settings, [key]: event.target.valueAsNumber })} /></label>)}</div>
      <label className="grid gap-2 text-sm">Additional sensitive field names (one per line)<textarea className="min-h-24 rounded-md border bg-background p-3" value={settings.sensitiveFields.join("\n")} onChange={event => setSettings({ ...settings, sensitiveFields: event.target.value.split("\n") })} onBlur={() => setSettings({ ...settings, sensitiveFields: settings.sensitiveFields.map(value => value.trim()).filter(Boolean) })} /></label>
      <label className="grid gap-2 text-sm">Excluded endpoint prefixes (one /api/ path per line)<textarea className="min-h-24 rounded-md border bg-background p-3" value={settings.excludedPrefixes.join("\n")} onChange={event => setSettings({ ...settings, excludedPrefixes: event.target.value.split("\n") })} onBlur={() => setSettings({ ...settings, excludedPrefixes: settings.excludedPrefixes.map(value => value.trim()).filter(Boolean) })} /></label>
      <p className="text-sm text-muted-foreground">Built-in redaction cannot be disabled. Bodies are restricted to workspace endpoints; all string values are redacted. Authentication, account, directory, configuration and diagnostic bodies are never stored. Headers use a fixed allowlist. Retention deletes expired records in bounded batches. Trace export requires OTEL_EXPORTER_OTLP_ENDPOINT on the API host; restart the API after changing that environment variable.</p>
      <Button type="submit" disabled={pending} className="w-fit">{pending ? "Saving…" : "Save configuration"}</Button>
    </form>}
  </CardContent></Card>
}
