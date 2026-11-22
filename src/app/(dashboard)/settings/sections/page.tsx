"use client"

import * as React from "react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { toast } from "@/components/ui/toast"
import {
  createSectionCode,
  deleteSectionCode,
  getLabCodes,
  getSectionCodes,
  updateSectionCode,
} from "@/lib/api/org"

type Lab = Awaited<ReturnType<typeof getLabCodes>>[number]
type Section = Awaited<ReturnType<typeof getSectionCodes>>[number]

export default function SectionCodeManagementPage() {
  const [labs, setLabs] = React.useState<Lab[]>([])
  const [sections, setSections] = React.useState<Section[]>([])
  const [editingId, setEditingId] = React.useState<string | null>(null)
  const [code, setCode] = React.useState("")
  const [name, setName] = React.useState("")
  const [description, setDescription] = React.useState("")
  const [isActive, setIsActive] = React.useState(true)

  async function refresh() {
    const [nextLabs, nextSections] = await Promise.all([
      getLabCodes(true),
      getSectionCodes(undefined, true),
    ])
    setLabs(nextLabs)
    setSections(nextSections)
  }

  React.useEffect(() => {
    Promise.all([getLabCodes(true), getSectionCodes(undefined, true)]).then(([nextLabs, nextSections]) => {
      setLabs(nextLabs)
      setSections(nextSections)
    }).catch((error) =>
      toast.add({
        type: "error",
        title: "Could not load organizational codes",
        description: error instanceof Error ? error.message : "Access denied.",
      })
    )
  }, [])

  function reset() {
    setEditingId(null)
    setCode("")
    setName("")
    setDescription("")
    setIsActive(true)
  }

  async function save() {
    try {
      if (editingId) {
        await updateSectionCode(editingId, { code, name, description, isActive })
        toast.add({ type: "success", title: "Section Code updated" })
      } else {
        await createSectionCode({ code, name, description, isActive })
        toast.add({ type: "success", title: "Section Code created" })
      }
      reset()
      await refresh()
    } catch (error) {
      toast.add({
        type: "error",
        title: "Could not save Section Code",
        description: error instanceof Error ? error.message : "Please try again.",
      })
    }
  }

  async function remove(section: Section) {
    if (!window.confirm(`Delete Section Code "${section.code}"?`)) return
    try {
      await deleteSectionCode(section.id)
      toast.add({ type: "success", title: "Section Code deleted" })
      await refresh()
    } catch (error) {
      toast.add({
        type: "error",
        title: "Section Code could not be deleted",
        description: error instanceof Error ? error.message : "Please try again.",
      })
    }
  }

  return (
    <div className="flex flex-col gap-6 px-4 lg:px-6">
      <div>
        <h1 className="text-3xl font-bold">Section Code Management</h1>
        <p className="text-muted-foreground">Create and manage Sections within Lab Codes.</p>
      </div>
      <Card>
        <CardHeader>
          <CardTitle>{editingId ? "Edit Section Code" : "Create Section Code"}</CardTitle>
          <CardDescription>Section Codes are independent organizational classifications.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-2">
          <div className="flex flex-col gap-2">
            <Label htmlFor="section-code">Section Code</Label>
            <Input id="section-code" value={code} onChange={(event) => setCode(event.target.value)} />
          </div>
          <div className="flex flex-col gap-2 md:col-span-2">
            <Label htmlFor="section-name">Section Name</Label>
            <Input id="section-name" value={name} onChange={(event) => setName(event.target.value)} />
          </div>
          <div className="flex flex-col gap-2 md:col-span-2">
            <Label htmlFor="section-description">Description</Label>
            <Textarea id="section-description" value={description} onChange={(event) => setDescription(event.target.value)} />
          </div>
          <label className="flex items-center gap-2 text-sm md:col-span-2">
            <input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />
            Active
          </label>
          <div className="flex gap-2 md:col-span-2">
            <Button type="button" onClick={save}>{editingId ? "Save Changes" : "Create Section Code"}</Button>
            {editingId && <Button type="button" variant="outline" onClick={reset}>Cancel</Button>}
          </div>
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>Section Codes</CardTitle>
          <CardDescription>{sections.length} Section Code{sections.length === 1 ? "" : "s"}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {sections.map((section) => {
            return (
              <div key={section.id} className="rounded-lg border p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h2 className="font-semibold">{section.code}</h2>
                    <p className="text-sm">{section.name}</p>
                    <p className="mt-1 text-xs text-muted-foreground">{section.description}</p>
                  </div>
                  <Badge variant={section.isActive ? "secondary" : "outline"}>
                    {section.isActive ? "Active" : "Inactive"}
                  </Badge>
                </div>
                <div className="mt-4 flex gap-2">
                  <Button type="button" size="sm" variant="outline" onClick={() => {
                    setEditingId(section.id)
                    setCode(section.code)
                    setName(section.name)
                    setDescription(section.description)
                    setIsActive(section.isActive)
                  }}>Edit</Button>
                  <Button type="button" size="sm" variant="destructive" onClick={() => remove(section)}>Delete</Button>
                </div>
              </div>
            )
          })}
        </CardContent>
      </Card>
    </div>
  )
}
