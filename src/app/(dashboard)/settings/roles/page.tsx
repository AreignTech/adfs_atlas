"use client"

import * as React from "react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { toast } from "@/components/ui/toast"
import {
  createRbacRole,
  deleteRbacRole,
  getRbacPermissions,
  getRbacRoles,
  updateRbacRole,
} from "@/db/actions/rbac"

type Role = Awaited<ReturnType<typeof getRbacRoles>>[number]
type Permission = Awaited<ReturnType<typeof getRbacPermissions>>[number]

export default function RoleManagementPage() {
  const [roles, setRoles] = React.useState<Role[]>([])
  const [permissions, setPermissions] = React.useState<Permission[]>([])
  const [name, setName] = React.useState("")
  const [description, setDescription] = React.useState("")
  const [selectedPermissions, setSelectedPermissions] = React.useState<string[]>([])
  const [editingId, setEditingId] = React.useState<string | null>(null)
  const [loading, setLoading] = React.useState(true)

  async function refresh() {
    const [nextRoles, nextPermissions] = await Promise.all([
      getRbacRoles(),
      getRbacPermissions(),
    ])
    setRoles(nextRoles)
    setPermissions(nextPermissions)
  }

  React.useEffect(() => {
    refresh()
      .catch((error) => {
        toast.add({
          type: "error",
          title: "Could not load roles",
          description: error instanceof Error ? error.message : "Access denied.",
        })
      })
      .finally(() => setLoading(false))
  }, [])

  function resetForm() {
    setEditingId(null)
    setName("")
    setDescription("")
    setSelectedPermissions([])
  }

  function editRole(role: Role) {
    setEditingId(role.id)
    setName(role.name)
    setDescription(role.description)
    setSelectedPermissions(role.permissionKeys)
  }

  async function saveRole() {
    try {
      if (editingId) {
        await updateRbacRole(editingId, {
          name,
          description,
          permissionKeys: selectedPermissions,
        })
        toast.add({ type: "success", title: "Role updated" })
      } else {
        await createRbacRole({
          name,
          description,
          permissionKeys: selectedPermissions,
        })
        toast.add({ type: "success", title: "Role created" })
      }
      resetForm()
      await refresh()
    } catch (error) {
      toast.add({
        type: "error",
        title: "Could not save role",
        description: error instanceof Error ? error.message : "Please try again.",
      })
    }
  }

  async function deleteRole(role: Role) {
    if (!window.confirm(`Delete role "${role.name}"? This cannot be undone.`)) {
      return
    }
    try {
      await deleteRbacRole(role.id)
      toast.add({ type: "success", title: "Role deleted" })
      await refresh()
    } catch (error) {
      toast.add({
        type: "error",
        title: "Role could not be deleted",
        description: error instanceof Error ? error.message : "Please try again.",
      })
    }
  }

  return (
    <div className="flex flex-col gap-6 px-4 lg:px-6">
      <div>
        <h1 className="text-3xl font-bold">Role Management</h1>
        <p className="text-muted-foreground">
          Create roles, configure permissions, and manage system role access.
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>{editingId ? "Edit Role" : "Create Role"}</CardTitle>
          <CardDescription>
            Role names are unique. Default system roles are protected from deletion.
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="grid gap-4 md:grid-cols-2">
            <div className="flex flex-col gap-2">
              <Label htmlFor="role-name">Role name</Label>
              <Input
                id="role-name"
                value={name}
                onChange={(event) => setName(event.target.value)}
                placeholder="e.g. Auditors"
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor="role-description">Description</Label>
              <Textarea
                id="role-description"
                value={description}
                onChange={(event) => setDescription(event.target.value)}
                placeholder="Describe this role."
              />
            </div>
          </div>
          <div>
            <Label>Permissions</Label>
            <div className="mt-3 grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
              {permissions.map((permission) => (
                <label
                  key={permission.id}
                  className="flex items-start gap-2 rounded-md border p-3 text-sm"
                >
                  <input
                    type="checkbox"
                    checked={selectedPermissions.includes(permission.permissionKey)}
                    onChange={(event) =>
                      setSelectedPermissions((current) =>
                        event.target.checked
                          ? [...current, permission.permissionKey]
                          : current.filter(
                              (key) => key !== permission.permissionKey
                            )
                      )
                    }
                  />
                  <span>
                    <span className="block font-medium">
                      {permission.permissionKey}
                    </span>
                    <span className="text-xs text-muted-foreground">
                      {permission.description}
                    </span>
                  </span>
                </label>
              ))}
            </div>
          </div>
          <div className="flex gap-2">
            <Button type="button" onClick={saveRole}>
              {editingId ? "Save Changes" : "Create Role"}
            </Button>
            {editingId && (
              <Button type="button" variant="outline" onClick={resetForm}>
                Cancel
              </Button>
            )}
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Application Roles</CardTitle>
          <CardDescription>
            {roles.length} role{roles.length === 1 ? "" : "s"} available
          </CardDescription>
        </CardHeader>
        <CardContent className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
          {loading ? (
            <p className="text-sm text-muted-foreground">Loading roles...</p>
          ) : (
            roles.map((role) => (
              <div key={role.id} className="rounded-lg border p-4">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h2 className="font-semibold">{role.name}</h2>
                    <p className="mt-1 text-sm text-muted-foreground">
                      {role.description}
                    </p>
                  </div>
                  {role.isSystemRole && (
                    <Badge variant="secondary">System</Badge>
                  )}
                </div>
                <p className="mt-3 text-xs text-muted-foreground">
                  {role.assignedUsers} assigned user
                  {role.assignedUsers === 1 ? "" : "s"} ·{" "}
                  {role.permissionKeys.length} permissions
                </p>
                {role.assignedUserNames.length > 0 && (
                  <p className="mt-2 text-xs text-muted-foreground">
                    {role.assignedUserNames.join(", ")}
                  </p>
                )}
                <div className="mt-4 flex gap-2">
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    onClick={() => editRole(role)}
                  >
                    Edit
                  </Button>
                  {!role.isSystemRole && (
                    <Button
                      type="button"
                      size="sm"
                      variant="destructive"
                      onClick={() => deleteRole(role)}
                    >
                      Delete
                    </Button>
                  )}
                </div>
              </div>
            ))
          )}
        </CardContent>
      </Card>
    </div>
  )
}
