"use client"

import * as React from "react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { toast } from "@/components/ui/toast"
import {
  createLabCode,
  deleteLabCode,
  getLabCodes,
  updateLabCode,
} from "@/db/actions/org"

type Lab = Awaited<ReturnType<typeof getLabCodes>>[number]

export default function LabCodeManagementPage() {
  const [labs, setLabs] = React.useState<Lab[]>([])
  const [editingId, setEditingId] = React.useState<string | null>(null)
  const [code, setCode] = React.useState("")
  const [name, setName] = React.useState("")
  const [description, setDescription] = React.useState("")
  const [isActive, setIsActive] = React.useState(true)

  async function refresh() {
    setLabs(await getLabCodes(true))
  }

  React.useEffect(() => {
    refresh().catch((error) =>
      toast.add({
        type: "error",
        title: "Could not load Lab Codes",
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
        await updateLabCode(editingId, { code, name, description, isActive })
        toast.add({ type: "success", title: "Lab Code updated" })
      } else {
        await createLabCode({ code, name, description, isActive })
        toast.add({ type: "success", title: "Lab Code created" })
      }
      reset()
      await refresh()
    } catch (error) {
      toast.add({
        type: "error",
        title: "Could not save Lab Code",
        description: error instanceof Error ? error.message : "Please try again.",
      })
    }
  }

  async function remove(lab: Lab) {
    if (!window.confirm(`Delete Lab Code "${lab.code}"?`)) return
    try {
      await deleteLabCode(lab.id)
      toast.add({ type: "success", title: "Lab Code deleted" })
      await refresh()
    } catch (error) {
      toast.add({
        type: "error",
        title: "Lab Code could not be deleted",
        description: error instanceof Error ? error.message : "Please try again.",
      })
    }
  }

  return (
    <div className="flex flex-col gap-6 px-4 lg:px-6">
      <div>
        <h1 className="text-3xl font-bold">Lab Code Management</h1>
        <p className="text-muted-foreground">Create and manage organizational Lab Codes.</p>
      </div>
      <Card>
        <CardHeader>
          <CardTitle>{editingId ? "Edit Lab Code" : "Create Lab Code"}</CardTitle>
          <CardDescription>Lab Codes identify organizational units.</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-4 md:grid-cols-2">
          <div className="flex flex-col gap-2">
            <Label htmlFor="lab-code">Lab Code</Label>
            <Input id="lab-code" value={code} onChange={(event) => setCode(event.target.value)} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="lab-name">Lab Name</Label>
            <Input id="lab-name" value={name} onChange={(event) => setName(event.target.value)} />
          </div>
          <div className="flex flex-col gap-2 md:col-span-2">
            <Label htmlFor="lab-description">Description</Label>
            <Textarea id="lab-description" value={description} onChange={(event) => setDescription(event.target.value)} />
          </div>
          <label className="flex items-center gap-2 text-sm md:col-span-2">
            <input type="checkbox" checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />
            Active
          </label>
          <div className="flex gap-2 md:col-span-2">
            <Button type="button" onClick={save}>{editingId ? "Save Changes" : "Create Lab Code"}</Button>
            {editingId && <Button type="button" variant="outline" onClick={reset}>Cancel</Button>}
          </div>
        </CardContent>
      </Card>
      <Card>
        <CardHeader>
          <CardTitle>Lab Codes</CardTitle>
          <CardDescription>{labs.length} Lab Code{labs.length === 1 ? "" : "s"}</CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {labs.map((lab) => (
            <div key={lab.id} className="rounded-lg border p-4">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <h2 className="font-semibold">{lab.code}</h2>
                  <p className="text-sm">{lab.name}</p>
                  <p className="mt-1 text-xs text-muted-foreground">{lab.description}</p>
                </div>
                <Badge variant={lab.isActive ? "secondary" : "outline"}>
                  {lab.isActive ? "Active" : "Inactive"}
                </Badge>
              </div>
              <div className="mt-4 flex gap-2">
                <Button type="button" size="sm" variant="outline" onClick={() => {
                  setEditingId(lab.id)
                  setCode(lab.code)
                  setName(lab.name)
                  setDescription(lab.description)
                  setIsActive(lab.isActive)
                }}>Edit</Button>
                <Button type="button" size="sm" variant="destructive" onClick={() => remove(lab)}>Delete</Button>
              </div>
            </div>
          ))}
        </CardContent>
      </Card>
    </div>
  )
}
