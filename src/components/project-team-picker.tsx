"use client"

import { useEffect, useState } from "react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Field, FieldLabel } from "@/components/ui/field"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { useFlowBoard } from "@/data/flowboard-store"
import { type Member } from "@/data/flowboard"
import { getProjectTeamOptions } from "@/lib/api/project-team"
import { cn } from "@/lib/utils"

const ALL = "__all__"
const UNASSIGNED = "__unassigned__"

export function ProjectTeamPicker({ selectedIds, onToggle }: { selectedIds: string[]; onToggle: (id: string) => void }) {
  const { members } = useFlowBoard()
  const [directory, setDirectory] = useState<Member[]>([])
  const [search, setSearch] = useState("")
  const [lab, setLab] = useState(ALL)
  const [section, setSection] = useState(ALL)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState("")
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    getProjectTeamOptions(controller.signal)
      .then(setDirectory)
      .catch((err) => { if (!controller.signal.aborted) setError(err instanceof Error ? err.message : "Could not load teammates.") })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [attempt])

  const candidates = Array.from(new Map([...members, ...directory].map((member) => [member.id, member])).values())
    .filter((member) => member.status !== "Inactive" || selectedIds.includes(member.id))
    .sort((a, b) => a.name.localeCompare(b.name))
  const labs = options(candidates, "lab")
  const sections = options(candidates, "section")
  const query = search.trim().toLocaleLowerCase()
  const filtered = candidates.filter((member) =>
    (lab === ALL || (lab === UNASSIGNED ? !member.labId : member.labId === lab)) &&
    (section === ALL || (section === UNASSIGNED ? !member.sectionId : member.sectionId === section)) &&
    (!query || `${member.name} ${member.email} ${member.labCode ?? ""} ${member.labName ?? ""} ${member.sectionCode ?? ""} ${member.sectionName ?? ""}`.toLocaleLowerCase().includes(query))
  )

  return <section aria-label="Project team" className="space-y-3">
    <div className="flex items-center justify-between gap-2">
      <h3 className="text-sm font-medium">Team ({selectedIds.length} selected)</h3>
      <Button type="button" variant="ghost" size="sm" onClick={() => { setSearch(""); setLab(ALL); setSection(ALL) }}>Clear filters</Button>
    </div>
    <Field>
      <FieldLabel htmlFor="project-team-search">Search teammates</FieldLabel>
      <Input id="project-team-search" placeholder="Search by name or email…" value={search} onChange={(event) => setSearch(event.target.value)} />
    </Field>
    <div className="grid gap-3 sm:grid-cols-2">
      <Field>
        <FieldLabel htmlFor="project-team-lab">Lab Code</FieldLabel>
        <Select items={[{ value: ALL, label: "All labs" }, { value: UNASSIGNED, label: "Unassigned lab" }, ...labs]} value={lab} onValueChange={(value) => { if (value) setLab(value) }}>
          <SelectTrigger id="project-team-lab" className="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>All labs</SelectItem>
            <SelectItem value={UNASSIGNED}>Unassigned lab</SelectItem>
            {labs.map((entry) => <SelectItem key={entry.value} value={entry.value}>{entry.label}</SelectItem>)}
          </SelectContent>
        </Select>
      </Field>
      <Field>
        <FieldLabel htmlFor="project-team-section">Section Code</FieldLabel>
        <Select items={[{ value: ALL, label: "All sections" }, { value: UNASSIGNED, label: "Unassigned section" }, ...sections]} value={section} onValueChange={(value) => { if (value) setSection(value) }}>
          <SelectTrigger id="project-team-section" className="w-full"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL}>All sections</SelectItem>
            <SelectItem value={UNASSIGNED}>Unassigned section</SelectItem>
            {sections.map((entry) => <SelectItem key={entry.value} value={entry.value}>{entry.label}</SelectItem>)}
          </SelectContent>
        </Select>
      </Field>
    </div>
    {selectedIds.length > 0 && <div className="flex flex-wrap gap-2" aria-label="Selected teammates">
      {selectedIds.map((id) => <Button key={id} type="button" size="sm" variant="secondary" aria-label={`Remove ${candidates.find((member) => member.id === id)?.name ?? "unavailable teammate"}`} onClick={() => onToggle(id)}>
        {candidates.find((member) => member.id === id)?.name ?? "Unavailable teammate"} ×
      </Button>)}
    </div>}
    {loading && <p role="status" className="text-sm text-muted-foreground">Loading teammates…</p>}
    {error && <div role="alert" className="space-y-2 text-sm text-destructive">
      <p>{error}</p>
      <Button type="button" size="sm" variant="outline" onClick={() => { setError(""); setLoading(true); setAttempt((current) => current + 1) }}>Retry loading teammates</Button>
    </div>}
    <p className="text-xs text-muted-foreground" aria-live="polite">{filtered.length} matching teammate{filtered.length === 1 ? "" : "s"}. Selections are kept when filters change.</p>
    <div className="grid max-h-52 gap-2 overflow-y-auto sm:grid-cols-2">
      {filtered.map((member) => <button key={member.id} type="button" aria-label={member.name} aria-pressed={selectedIds.includes(member.id)} onClick={() => onToggle(member.id)} className={cn(
        "min-w-0 cursor-pointer rounded-md border px-3 py-2 text-left text-sm hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
        selectedIds.includes(member.id) && "border-primary bg-primary/10"
      )}>
        <span className="block truncate font-medium">{member.name}</span>
        <span className="block truncate text-xs text-muted-foreground">{member.email}</span>
        <span className="block truncate text-xs text-muted-foreground">{label(member, "lab")} · {label(member, "section")}</span>
      </button>)}
    </div>
    {!loading && !error && filtered.length === 0 && <p className="text-sm text-muted-foreground">No teammates match these filters.</p>}
  </section>
}

function label(member: Member, kind: "lab" | "section") {
  const code = member[kind === "lab" ? "labCode" : "sectionCode"]
  const name = member[kind === "lab" ? "labName" : "sectionName"]
  return [code, name].filter(Boolean).join(" - ") || `Unassigned ${kind}`
}

function options(members: Member[], kind: "lab" | "section") {
  return Array.from(new Map(members.flatMap((member) => {
    const id = member[kind === "lab" ? "labId" : "sectionId"]
    return id ? [[id, { value: id, label: label(member, kind) }] as const] : []
  })).values()).sort((a, b) => a.label.localeCompare(b.label))
}
