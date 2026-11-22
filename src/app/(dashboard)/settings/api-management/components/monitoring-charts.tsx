"use client"

import { ResponsiveContainer, BarChart, Bar, LineChart, Line, XAxis, YAxis, Tooltip, Legend, CartesianGrid, PieChart, Pie, Cell } from "recharts"
import { Card, CardHeader, CardTitle, CardContent } from "@/components/ui/card"
import type { Statistics } from "@/lib/api/monitoring"

function ChartCard({ title, children }: { title: string; children: React.ReactNode }) {
  return <Card className="min-w-0"><CardHeader><CardTitle className="text-base">{title}</CardTitle></CardHeader><CardContent><div className="h-64 w-full" role="img" aria-label={title}>{children}</div></CardContent></Card>
}
export function HealthCharts({ statistics: data }: { statistics: Statistics }) {
  const timeline = data.timeline.map(row => ({ ...row, label: new Date(row.label).toLocaleString() }))
  const outcomes = [{ name: "Successful", value: data.summary.successful }, { name: "Failed", value: data.summary.failed }]
  return <div className="grid gap-4 lg:grid-cols-2">
    <ChartCard title="API Requests Over Time"><ResponsiveContainer initialDimension={{ width: 400, height: 256 }}><BarChart data={timeline}><CartesianGrid strokeDasharray="3 3" /><XAxis dataKey="label" hide /><YAxis /><Tooltip /><Bar dataKey="total" name="Requests" fill="var(--primary)" /></BarChart></ResponsiveContainer></ChartCard>
    <ChartCard title="Successful vs. Failed Requests"><ResponsiveContainer initialDimension={{ width: 400, height: 256 }}><PieChart><Pie data={outcomes} dataKey="value" nameKey="name" label={({ name, value }) => `${name}: ${value}`}><Cell fill="var(--primary)" /><Cell fill="var(--destructive)" /></Pie><Tooltip /><Legend /></PieChart></ResponsiveContainer></ChartCard>
    <ChartCard title="API Response Time Trends (ms)"><ResponsiveContainer initialDimension={{ width: 400, height: 256 }}><LineChart data={timeline}><CartesianGrid strokeDasharray="3 3" /><XAxis dataKey="label" hide /><YAxis /><Tooltip /><Legend /><Line type="monotone" dataKey="averageMs" name="Average" stroke="var(--primary)" dot={false} /><Line type="monotone" dataKey="p95Ms" name="P95" stroke="var(--destructive)" dot={false} /></LineChart></ResponsiveContainer></ChartCard>
    <ChartCard title="API Error Distribution"><ResponsiveContainer initialDimension={{ width: 400, height: 256 }}><BarChart data={data.statusCodes.filter(row => Number(row.label) >= 400)}><XAxis dataKey="label" /><YAxis /><Tooltip /><Bar dataKey="total" name="Errors">{data.statusCodes.filter(row => Number(row.label) >= 400).map(row => <Cell key={row.label} fill={Number(row.label) >= 500 ? "var(--destructive)" : "var(--primary)"} />)}</Bar></BarChart></ResponsiveContainer></ChartCard>
    {[["Most Frequently Accessed Endpoints", data.endpoints, "total"], ["Slowest Endpoints (average ms)", data.slowestEndpoints, "averageMs"]].map(([title, rows, key]) => <Card key={String(title)} className="min-w-0"><CardHeader><CardTitle className="text-base">{String(title)}</CardTitle></CardHeader><CardContent><ul className="grid gap-3 text-sm">{(rows as Statistics["endpoints"]).map(row => <li key={row.label} className="flex justify-between gap-3"><span className="break-all font-mono text-xs">{row.label}</span><span>{key === "total" ? row.total : row.averageMs.toFixed(1)}</span></li>)}</ul></CardContent></Card>)}
    <Card><CardHeader><CardTitle className="text-base">Requests by Method and Status</CardTitle></CardHeader><CardContent className="flex flex-wrap gap-4 text-sm">{[...data.methods, ...data.statusCodes].map(row => <span key={row.label}>{row.label}: {row.total}</span>)}</CardContent></Card>
  </div>
}
