"use client"

import { type FormEvent, useEffect, useMemo, useState } from "react"
import {
  type ColumnDef,
  type ColumnFiltersState,
  type ColumnVisibilityState,
  type Row,
  type SortingState,
  flexRender,
  useTable,
} from "@tanstack/react-table"

import { dataTableFeatures, type DataTableFeatures } from "@/lib/data-table"
import {
  ChevronDown,
  Download,
  EllipsisVertical,
  KeyRound,
  Pencil,
  Search,
  Trash2,
  UserCheck,
  UserX,
} from "lucide-react"

import { Avatar, AvatarFallback } from "@/components/ui/avatar"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Field, FieldError, FieldGroup, FieldLabel } from "@/components/ui/field"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from "@/components/ui/input-group"
import { Progress } from "@/components/ui/progress"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { toast } from "@/components/ui/toast"
import { cn } from "@/lib/utils"
import { useFlowBoard } from "@/data/flowboard-store"
import { type Member, type MemberStatus } from "@/data/flowboard"
import { getRbacRoles } from "@/lib/api/rbac"
import { createInvitationLink } from "@/lib/api/members"
import { adminPasswordResetSchema, memberProfileUpdateSchema } from "@/lib/api/contracts"
import { Input } from "@/components/ui/input"
import {
  assignUserOrg,
  getLabCodes,
  getSectionCodes,
} from "@/lib/api/org"

const statusClasses: Record<MemberStatus, string> = {
  Active: "border-success/40 text-success bg-success/5",
  Invited: "border-warning/40 text-warning bg-warning/5",
  Away: "border-border text-muted-foreground bg-muted/50",
  Inactive: "border-destructive/40 text-destructive bg-destructive/5",
}

const memberStatuses: MemberStatus[] = ["Active", "Invited", "Away", "Inactive"]

const roleColors: Record<string, string> = {
  "Product Lead": "text-destructive bg-destructive/10",
  "Engineering Lead": "text-info bg-info/10",
  "Product Designer": "text-warning bg-warning/10",
  "Data Analyst": "text-success bg-success/10",
}

export function TeamTable() {
  const { members, projects, workloadFor, actions, isAdministrator, currentMember } =
    useFlowBoard()

  const [sorting, setSorting] = useState<SortingState>([])
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([])
  const [columnVisibility, setColumnVisibility] =
    useState<ColumnVisibilityState>({})
  const [rowSelection, setRowSelection] = useState({})
  const [globalFilter, setGlobalFilter] = useState("")
  const [removeMember, setRemoveMember] = useState<Member | null>(null)
  const [invitationLink, setInvitationLink] = useState("")
  const [deleting, setDeleting] = useState(false)
  const [busyMember, setBusyMember] = useState<string | null>(null)
  const [editMember, setEditMember] = useState<Member | null>(null)
  const [resetPasswordMember, setResetPasswordMember] = useState<Member | null>(
    null
  )
  const [statusChange, setStatusChange] = useState<{
    member: Member
    next: MemberStatus
  } | null>(null)
  const [statusChanging, setStatusChanging] = useState(false)
  const [rbacRoles, setRbacRoles] = useState<
    Awaited<ReturnType<typeof getRbacRoles>>
  >([])
  const [labCodes, setLabCodes] = useState<
    Awaited<ReturnType<typeof getLabCodes>>
  >([])
  const [sectionCodes, setSectionCodes] = useState<
    Awaited<ReturnType<typeof getSectionCodes>>
  >([])
  const [orgDrafts, setOrgDrafts] = useState<
    Record<string, { labId: string; sectionId: string }>
  >({})

  const [syncedMembers, setSyncedMembers] = useState(members)
  if (syncedMembers !== members) {
    setSyncedMembers(members)
    setOrgDrafts((current) => {
      const next = { ...current }
      for (const member of members) {
        if (!next[member.id]) {
          next[member.id] = {
            labId: member.labId ?? "",
            sectionId: member.sectionId ?? "",
          }
        }
      }
      return next
    })
  }

  useEffect(() => {
    if (isAdministrator) {
      Promise.all([
        getRbacRoles(),
        getLabCodes(true),
        getSectionCodes(undefined, true),
      ])
        .then(([roles, labs, sections]) => {
          setRbacRoles(roles)
          setLabCodes(labs)
          setSectionCodes(sections)
        })
        .catch(() => {
          setRbacRoles([])
          setLabCodes([])
          setSectionCodes([])
        })
    }
  }, [isAdministrator])

  const roles = useMemo(
    () => Array.from(new Set(members.map((member) => member.role))),
    [members]
  )

  const maxWorkload = useMemo(
    () => Math.max(...members.map((member) => workloadFor(member.id).assigned), 1),
    [members, workloadFor]
  )

  const exactFilter = (
    row: Row<DataTableFeatures, Member>,
    columnId: string,
    value: string
  ) => row.getValue(columnId) === value

  const columns = useMemo<ColumnDef<DataTableFeatures, Member>[]>(
    () => [
      {
        id: "select",
        header: ({ table }) => (
          <div className="flex items-center justify-center px-2">
            <Checkbox
              checked={table.getIsAllPageRowsSelected()}
              indeterminate={
                table.getIsSomePageRowsSelected() &&
                !table.getIsAllPageRowsSelected()
              }
              onCheckedChange={(value) =>
                table.toggleAllPageRowsSelected(!!value)
              }
              aria-label="Select all"
            />
          </div>
        ),
        cell: ({ row }) => (
          <div className="flex items-center justify-center px-2">
            <Checkbox
              checked={row.getIsSelected()}
              onCheckedChange={(value) => row.toggleSelected(!!value)}
              aria-label="Select row"
            />
          </div>
        ),
        enableSorting: false,
        enableHiding: false,
      },
      {
        accessorKey: "name",
        header: "Teammate",
        cell: ({ row }) => {
          const member = row.original
          return (
            <div className="flex items-center gap-3">
              <Avatar className="size-8">
                <AvatarFallback className="text-xs font-medium">
                  {member.initials}
                </AvatarFallback>
              </Avatar>
              <div className="flex flex-col">
                <span className="font-medium">{member.name}</span>
                <span className="text-sm text-muted-foreground">
                  {member.email}
                </span>
              </div>
            </div>
          )
        },
      },
      {
        accessorKey: "role",
        header: "Role",
        cell: ({ row }) => {
          const role = row.getValue("role") as string
          if (isAdministrator) {
            const roleId =
              rbacRoles.find((entry) => entry.name === role)?.id ?? ""
            return (
              <Select
                value={roleId}
                items={rbacRoles.map((entry) => ({
                  value: entry.id,
                  label: entry.name,
                }))}
                onValueChange={async (value) => {
                  if (!value || value === roleId) return
                  try {
                    await actions.assignMemberRole(row.original.id, value)
                    toast.add({
                      type: "success",
                      title: "Role changed",
                      description: `${row.original.name} now has the selected role.`,
                    })
                  } catch (error) {
                    toast.add({
                      type: "error",
                      title: "Could not change role",
                      description:
                        error instanceof Error
                          ? error.message
                          : "Please try again.",
                    })
                  }
                }}
              >
                <SelectTrigger className="h-8 w-[180px]">
                  <SelectValue placeholder={role}>{role}</SelectValue>
                </SelectTrigger>
                <SelectContent>
                  {rbacRoles.map((entry) => (
                    <SelectItem key={entry.id} value={entry.id}>
                      {entry.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )
          }
          return (
            <Badge
              variant="secondary"
              className={cn("font-normal", roleColors[role])}
            >
              {role}
            </Badge>
          )
        },
        filterFn: exactFilter,
      },
      {
        id: "lab",
        header: "Lab Code",
        cell: ({ row }) => {
          const member = row.original
          if (!isAdministrator) {
            const lab = labCodes.find((entry) => entry.id === member.labId)
            return (
              <span className="text-sm">
                {lab ? `${lab.code} - ${lab.name}` : "Unassigned"}
              </span>
            )
          }
          return (
            <Select
              value={orgDrafts[member.id]?.labId ?? member.labId ?? ""}
              items={labCodes.map((lab) => ({
                value: lab.id,
                label: `${lab.code} - ${lab.name}`,
              }))}
              onValueChange={async (value) => {
                if (!value) return
                if (!member.userId) return
                const sectionId =
                  orgDrafts[member.id]?.sectionId ?? member.sectionId ?? ""
                setOrgDrafts((current) => ({
                  ...current,
                  [member.id]: {
                    labId: value,
                    sectionId,
                  },
                }))
                if (sectionId) {
                  try {
                    await assignUserOrg(member.userId, value, sectionId)
                    toast.add({ type: "success", title: "Lab assignment updated" })
                  } catch (error) {
                    toast.add({
                      type: "error",
                      title: "Could not assign Lab",
                      description: error instanceof Error ? error.message : "Please try again.",
                    })
                  }
                } else {
                  toast.add({
                    type: "info",
                    title: "Lab assignment staged",
                    description: "Select a Section to complete the assignment.",
                  })
                }
              }}
            >
              <SelectTrigger className="h-8 w-[150px]">
                <SelectValue placeholder="Unassigned">
                  {(() => {
                    const lab = labCodes.find(
                      (entry) => entry.id === (orgDrafts[member.id]?.labId ?? member.labId)
                    )
                    return lab ? `${lab.code} - ${lab.name}` : "Unassigned"
                  })()}
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {labCodes.filter((lab) => lab.isActive).map((lab) => (
                  <SelectItem key={lab.id} value={lab.id}>
                    {lab.code} - {lab.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )
        },
      },
      {
        id: "section",
        header: "Section Code",
        cell: ({ row }) => {
          const member = row.original
          const availableSections = sectionCodes.filter(
            (section) => section.isActive
          )
          if (!isAdministrator) {
            const section = sectionCodes.find(
              (entry) => entry.id === member.sectionId
            )
          return (
            <span className="text-sm">
              {section
                ? `${section.code} - ${section.name}`
                : "Unassigned"}
            </span>
          )
          }
          return (
            <Select
              value={orgDrafts[member.id]?.sectionId ?? member.sectionId ?? ""}
              items={sectionCodes.map((section) => ({
                value: section.id,
                label: `${section.code} - ${section.name}`,
              }))}
              onValueChange={async (value) => {
                if (!value) return
                if (!member.userId) return
                const labId =
                  orgDrafts[member.id]?.labId ?? member.labId ?? ""
                setOrgDrafts((current) => ({
                  ...current,
                  [member.id]: {
                    labId,
                    sectionId: value,
                  },
                }))
                if (!labId) {
                  toast.add({
                    type: "info",
                    title: "Section assignment staged",
                    description: "Select a Lab to complete the assignment.",
                  })
                  return
                }
                try {
                  await assignUserOrg(member.userId, labId, value)
                  toast.add({ type: "success", title: "Section assignment updated" })
                } catch (error) {
                  toast.add({
                    type: "error",
                    title: "Could not assign Section",
                    description: error instanceof Error ? error.message : "Please try again.",
                  })
                }
              }}
            >
              <SelectTrigger className="h-8 w-[150px]">
                <SelectValue placeholder="Unassigned">
                  {(() => {
                    const section = sectionCodes.find(
                      (entry) => entry.id === (orgDrafts[member.id]?.sectionId ?? member.sectionId)
                    )
                    return section ? `${section.code} - ${section.name}` : "Unassigned"
                  })()}
                </SelectValue>
              </SelectTrigger>
              <SelectContent>
                {availableSections.map((section) => (
                  <SelectItem key={section.id} value={section.id}>
                    {section.code} - {section.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )
        },
      },
      {
        id: "projects",
        header: "Projects",
        cell: ({ row }) => {
          const count = projects.filter((project) =>
            project.memberIds.includes(row.original.id)
          ).length

          return <span className="tabular-nums">{count}</span>
        },
      },
      {
        id: "workload",
        header: "Open workload",
        cell: ({ row }) => {
          const { assigned, completed } = workloadFor(row.original.id)

          return (
            <div className="flex w-[160px] flex-col gap-1.5">
              <div className="flex items-center justify-between text-xs text-muted-foreground">
                <span className="tabular-nums">{assigned} open</span>
                <span className="tabular-nums">{completed} done</span>
              </div>
              <Progress
                value={Math.round((assigned / maxWorkload) * 100)}
              />
            </div>
          )
        },
      },
      {
        accessorKey: "status",
        header: "Status",
        cell: ({ row }) => {
          const status = row.getValue("status") as MemberStatus
          return (
            <Badge variant="outline" className={statusClasses[status]}>
              {status}
            </Badge>
          )
        },
        filterFn: exactFilter,
      },
      {
        accessorKey: "lastActive",
        header: "Last active",
        cell: ({ row }) => (
          <span className="text-sm text-muted-foreground">
            {row.getValue("lastActive") as string}
          </span>
        ),
      },
      {
        id: "actions",
        enableHiding: false,
        header: "Actions",
        cell: ({ row }) => {
          const member = row.original
          const isCurrentUser = member.id === currentMember?.id

          return (
            <div className="flex items-center gap-2">
              <Button
                variant="ghost"
                size="icon"
                className="size-8 cursor-pointer"
                disabled={
                  member.status === "Invited" ||
                  member.status === "Inactive" ||
                  busyMember === member.id
                }
                onClick={async () => {
                  const nextStatus: MemberStatus =
                    member.status === "Active" ? "Away" : "Active"
                  setBusyMember(member.id)
                  try {
                    await actions.setMemberStatus(member.id, nextStatus)
                    toast.add({ type: "success", title: `Marked as ${nextStatus.toLowerCase()}`, description: member.name })
                  } catch (error) {
                    toast.add({ type: "error", title: "Could not update member", description: error instanceof Error ? error.message : "Please try again." })
                  } finally { setBusyMember(null) }
                }}
              >
                <Pencil data-icon="inline-start" />
                <span className="sr-only">Toggle presence for {member.name}</span>
              </Button>
              <DropdownMenu>
                <DropdownMenuTrigger
                  render={
                    <Button
                      variant="ghost"
                      size="icon"
                      className="size-8 cursor-pointer"
                    />
                  }
                >
                  <EllipsisVertical />
                  <span className="sr-only">More actions for {member.name}</span>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuGroup>
                    {member.status === "Invited" && <DropdownMenuItem
                      className="cursor-pointer"
                      disabled={busyMember === member.id}
                      onClick={async () => {
                        setBusyMember(member.id)
                        try { setInvitationLink(await createInvitationLink(member.id)) }
                        catch (error) { toast.add({ type: "error", title: "Could not create invitation link", description: error instanceof Error ? error.message : "Please try again." }) }
                        finally { setBusyMember(null) }
                      }}
                    >Create new invitation link</DropdownMenuItem>}
                  </DropdownMenuGroup>
                  {isAdministrator && member.status !== "Invited" && (
                    <>
                      <DropdownMenuSeparator />
                      <DropdownMenuGroup>
                        <DropdownMenuItem
                          className="cursor-pointer"
                          onClick={() => setEditMember(member)}
                        >
                          <Pencil />
                          Edit details
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          className="cursor-pointer"
                          onClick={() => setResetPasswordMember(member)}
                        >
                          <KeyRound />
                          Reset password
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          variant="destructive"
                          className="cursor-pointer"
                          onClick={() =>
                            setStatusChange({
                              member,
                              next:
                                member.status === "Inactive"
                                  ? "Active"
                                  : "Inactive",
                            })
                          }
                        >
                          {member.status === "Inactive" ? (
                            <UserCheck />
                          ) : (
                            <UserX />
                          )}
                          {member.status === "Inactive"
                            ? "Activate account"
                            : "Deactivate account"}
                        </DropdownMenuItem>
                      </DropdownMenuGroup>
                    </>
                  )}
                  <DropdownMenuSeparator />
                  <DropdownMenuGroup>
                    <DropdownMenuItem
                      variant="destructive"
                      className="cursor-pointer"
                      disabled={isCurrentUser}
                      onClick={() => setRemoveMember(member)}
                    >
                      <Trash2 />
                      Delete account
                    </DropdownMenuItem>
                  </DropdownMenuGroup>
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
          )
        },
      },
    ],
    [projects, workloadFor, maxWorkload, actions, isAdministrator, labCodes, orgDrafts, rbacRoles, sectionCodes, currentMember?.id, busyMember]
  )

  const table = useTable({
    features: dataTableFeatures,
    data: members,
    columns,
    globalFilterFn: (row, _columnId, value) => {
      const member = row.original
      return [member.name, member.email, member.role, member.status]
        .some((field) => field.toLowerCase().includes(String(value).trim().toLowerCase()))
    },
    onSortingChange: setSorting,
    onColumnFiltersChange: setColumnFilters,
    onColumnVisibilityChange: setColumnVisibility,
    onRowSelectionChange: setRowSelection,
    onGlobalFilterChange: setGlobalFilter,
    state: {
      sorting,
      columnFilters,
      columnVisibility,
      rowSelection,
      globalFilter,
    },
  })

  const statusFilter = table.getColumn("status")?.getFilterValue() as string
  const roleFilter = table.getColumn("role")?.getFilterValue() as string

  return (
    <div className="flex flex-col w-full gap-4">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <InputGroup className="flex-1 max-w-sm">
          <InputGroupInput
            placeholder="Search teammates..."
            value={globalFilter ?? ""}
            onChange={(event) => setGlobalFilter(String(event.target.value))}
          />
          <InputGroupAddon>
            <Search />
          </InputGroupAddon>
        </InputGroup>

        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            className="cursor-pointer"
            onClick={() =>
              toast.add({
                type: "info",
                title: "Export started",
                description:
                  "The roster CSV would download once the API is connected.",
              })
            }
          >
            <Download data-icon="inline-start" />
            Export
          </Button>
        </div>
      </div>

      <FieldGroup className="grid gap-2 sm:grid-cols-3 sm:gap-4">
        <Field>
          <FieldLabel htmlFor="status-filter" className="text-sm font-medium">
            Status
          </FieldLabel>
          <Select
            items={[
              { value: "all", label: "All statuses" },
              ...memberStatuses.map((status) => ({
                value: status,
                label: status,
              })),
            ]}
            value={statusFilter || "all"}
            onValueChange={(value) =>
              table
                .getColumn("status")
                ?.setFilterValue(value === "all" ? undefined : value)
            }
          >
            <SelectTrigger className="cursor-pointer w-full" id="status-filter">
              <SelectValue placeholder="All statuses" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="all">All statuses</SelectItem>
                {memberStatuses.map((status) => (
                  <SelectItem key={status} value={status}>
                    {status}
                  </SelectItem>
                ))}
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>

        <Field>
          <FieldLabel htmlFor="role-filter" className="text-sm font-medium">
            Role
          </FieldLabel>
          <Select
            items={[
              { value: "all", label: "All roles" },
              ...roles.map((role) => ({ value: role, label: role })),
            ]}
            value={roleFilter || "all"}
            onValueChange={(value) =>
              table
                .getColumn("role")
                ?.setFilterValue(value === "all" ? undefined : value)
            }
          >
            <SelectTrigger className="cursor-pointer w-full" id="role-filter">
              <SelectValue placeholder="All roles" />
            </SelectTrigger>
            <SelectContent>
              <SelectGroup>
                <SelectItem value="all">All roles</SelectItem>
                {roles.map((role) => (
                  <SelectItem key={role} value={role}>
                    {role}
                  </SelectItem>
                ))}
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>

        <Field>
          <FieldLabel htmlFor="column-visibility" className="text-sm font-medium">
            Columns
          </FieldLabel>
          <DropdownMenu>
            <DropdownMenuTrigger
              id="column-visibility"
              render={
                <Button variant="outline" className="cursor-pointer w-full" />
              }
            >
              Toggle columns <ChevronDown data-icon="inline-end" />
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuGroup>
                {table
                  .getAllColumns()
                  .filter((column) => column.getCanHide())
                  .map((column) => (
                    <DropdownMenuCheckboxItem
                      key={column.id}
                      className="capitalize cursor-pointer"
                      checked={column.getIsVisible()}
                      onCheckedChange={(value) =>
                        column.toggleVisibility(!!value)
                      }
                    >
                      {column.id}
                    </DropdownMenuCheckboxItem>
                  ))}
              </DropdownMenuGroup>
            </DropdownMenuContent>
          </DropdownMenu>
        </Field>
      </FieldGroup>

      <div className="rounded-md border">
        <Table>
          <TableHeader>
            {table.getHeaderGroups().map((headerGroup) => (
              <TableRow key={headerGroup.id}>
                {headerGroup.headers.map((header) => (
                  <TableHead key={header.id}>
                    {header.isPlaceholder
                      ? null
                      : flexRender(
                          header.column.columnDef.header,
                          header.getContext()
                        )}
                  </TableHead>
                ))}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {table.getRowModel().rows?.length ? (
              table.getRowModel().rows.map((row) => (
                <TableRow
                  key={row.id}
                  data-state={row.getIsSelected() && "selected"}
                >
                  {row.getVisibleCells().map((cell) => (
                    <TableCell key={cell.id}>
                      {flexRender(
                        cell.column.columnDef.cell,
                        cell.getContext()
                      )}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            ) : (
              <TableRow>
                <TableCell colSpan={columns.length} className="h-24 text-center">
                  No teammates match these filters.
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>

      <div className="flex items-center justify-between gap-2 py-4">
        <Field orientation="horizontal" className="w-fit">
          <FieldLabel htmlFor="page-size" className="text-sm font-medium">
            Show
          </FieldLabel>
          <Select
            value={`${table.state.pagination.pageSize}`}
            onValueChange={(value) => table.setPageSize(Number(value))}
          >
            <SelectTrigger className="w-20 cursor-pointer" id="page-size">
              <SelectValue placeholder={table.state.pagination.pageSize} />
            </SelectTrigger>
            <SelectContent side="top">
              <SelectGroup>
                {[10, 20, 30, 40, 50].map((pageSize) => (
                  <SelectItem key={pageSize} value={`${pageSize}`}>
                    {pageSize}
                  </SelectItem>
                ))}
              </SelectGroup>
            </SelectContent>
          </Select>
        </Field>
        <div className="flex-1 text-sm text-muted-foreground hidden sm:block">
          {table.getFilteredSelectedRowModel().rows.length} of{" "}
          {table.getFilteredRowModel().rows.length} row(s) selected.
        </div>
        <div className="flex items-center gap-x-6 lg:gap-x-8">
          <div className="hidden items-center gap-2 sm:flex">
            <p className="text-sm font-medium">Page</p>
            <strong className="text-sm">
              {table.state.pagination.pageIndex + 1} of {table.getPageCount()}
            </strong>
          </div>
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => table.previousPage()}
              disabled={!table.getCanPreviousPage()}
              className="cursor-pointer"
            >
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => table.nextPage()}
              disabled={!table.getCanNextPage()}
              className="cursor-pointer"
            >
              Next
            </Button>
          </div>
        </div>
      </div>

      <Dialog
        open={Boolean(removeMember)}
        onOpenChange={(open) => !open && !deleting && setRemoveMember(null)}
      >
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Delete {removeMember?.name}&apos;s account?</DialogTitle>
            <DialogDescription>
              This permanently deletes their account and revokes all sessions.
              Their tasks become unassigned. Any owned projects transfer to an
              active teammate in the permitted organization scope. Deletion is
              blocked if no active successor is available.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              className="cursor-pointer"
              onClick={() => setRemoveMember(null)}
              disabled={deleting}
            >
              Cancel
            </Button>
            <Button
              variant="destructive"
              className="cursor-pointer"
              disabled={deleting}
              onClick={async () => {
                if (removeMember) {
                  const name = removeMember.name
                  setDeleting(true)
                  try {
                    await actions.removeMember(removeMember.id)
                    setRemoveMember(null)
                    toast.add({ type: "success", title: "Account deleted", description: `${name} no longer has workspace access.` })
                  } catch (error) {
                    toast.add({ type: "error", title: "Could not delete account", description: error instanceof Error ? error.message : "Please try again." })
                  } finally { setDeleting(false) }
                }
              }}
            >
              {deleting ? "Deleting..." : "Delete account"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      <Dialog open={Boolean(invitationLink)} onOpenChange={(open) => !open && setInvitationLink("")}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Share invitation link</DialogTitle>
            <DialogDescription>Share this link privately with the invited teammate. It expires in seven days, works once, and replaces any previous link. They must register with their invited email address.</DialogDescription>
          </DialogHeader>
          <Input aria-label="Invitation link" readOnly value={invitationLink} onFocus={(event) => event.target.select()} />
          <DialogFooter>
            <Button onClick={async () => {
              try { await navigator.clipboard.writeText(invitationLink); toast.add({ type: "success", title: "Invitation link copied" }) }
              catch { toast.add({ type: "error", title: "Copy failed", description: "Select and copy the link above." }) }
            }}>Copy link</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {editMember && (
        <EditMemberDialog member={editMember} onClose={() => setEditMember(null)} />
      )}

      {resetPasswordMember && (
        <ResetPasswordDialog
          member={resetPasswordMember}
          onClose={() => setResetPasswordMember(null)}
        />
      )}

      <Dialog
        open={Boolean(statusChange)}
        onOpenChange={(open) => !open && !statusChanging && setStatusChange(null)}
      >
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>
              {statusChange?.next === "Inactive"
                ? `Deactivate ${statusChange?.member.name}'s account?`
                : `Reactivate ${statusChange?.member.name}'s account?`}
            </DialogTitle>
            <DialogDescription>
              {statusChange?.next === "Inactive"
                ? "They will be signed out of every device immediately and cannot sign in again until an administrator reactivates the account. Their tasks and history are preserved."
                : "They will be able to sign in again with their existing credentials."}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button
              variant="outline"
              className="cursor-pointer"
              disabled={statusChanging}
              onClick={() => setStatusChange(null)}
            >
              Cancel
            </Button>
            <Button
              variant={statusChange?.next === "Inactive" ? "destructive" : "default"}
              className="cursor-pointer"
              disabled={statusChanging}
              onClick={async () => {
                if (!statusChange) return
                const { member, next } = statusChange
                setStatusChanging(true)
                try {
                  await actions.setMemberStatus(member.id, next)
                  toast.add({
                    type: "success",
                    title:
                      next === "Inactive"
                        ? "Account deactivated"
                        : "Account reactivated",
                    description:
                      next === "Inactive"
                        ? `${member.name} was signed out of all devices and cannot sign in.`
                        : `${member.name} can sign in again.`,
                  })
                  setStatusChange(null)
                } catch (error) {
                  toast.add({
                    type: "error",
                    title: "Could not update the account status",
                    description:
                      error instanceof Error ? error.message : "Please try again.",
                  })
                } finally {
                  setStatusChanging(false)
                }
              }}
            >
              {statusChanging
                ? "Saving..."
                : statusChange?.next === "Inactive"
                  ? "Deactivate account"
                  : "Reactivate account"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}

function splitName(name: string): [string, string] {
  const index = name.indexOf(" ")
  return index === -1 ? [name, ""] : [name.slice(0, index), name.slice(index + 1)]
}

function EditMemberDialog({
  member,
  onClose,
}: {
  member: Member
  onClose: () => void
}) {
  const { actions } = useFlowBoard()
  const [firstName, setFirstName] = useState(() => splitName(member.name)[0])
  const [lastName, setLastName] = useState(() => splitName(member.name)[1])
  const [email, setEmail] = useState(member.email)
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (submitting) return
    const result = memberProfileUpdateSchema.safeParse({
      name: `${firstName} ${lastName}`.replace(/\s+/g, " ").trim(),
      email: email.trim(),
    })
    if (!result.success) {
      const nextErrors: Record<string, string> = {}
      for (const issue of result.error.issues) {
        const key = String(issue.path[0])
        if (!nextErrors[key]) nextErrors[key] = issue.message
      }
      setErrors(nextErrors)
      return
    }
    setErrors({})
    setSubmitting(true)
    try {
      await actions.updateMember(member.id, result.data)
      toast.add({
        type: "success",
        title: "Details updated",
        description: `${result.data.name}'s profile was saved.`,
      })
      onClose()
    } catch (error) {
      const message =
        error instanceof Error ? error.message : "Please try again."
      if (message.toLowerCase().includes("email")) {
        setErrors({ email: message })
      }
      toast.add({
        type: "error",
        title: "Could not update details",
        description: message,
      })
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && !submitting && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Edit details</DialogTitle>
          <DialogDescription>
            Update this teammate&apos;s name or email address. The email must be
            unique across the workspace.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="grid gap-4">
          <FieldGroup className="grid gap-4 sm:grid-cols-2">
            <Field>
              <FieldLabel htmlFor="edit-first-name">First name</FieldLabel>
              <Input
                id="edit-first-name"
                value={firstName}
                autoComplete="off"
                onChange={(event) => setFirstName(event.target.value)}
              />
            </Field>
            <Field data-invalid={!!errors.name}>
              <FieldLabel htmlFor="edit-last-name">Last name</FieldLabel>
              <Input
                id="edit-last-name"
                value={lastName}
                autoComplete="off"
                onChange={(event) => setLastName(event.target.value)}
              />
              {errors.name && <FieldError errors={[{ message: errors.name }]} />}
            </Field>
          </FieldGroup>
          <Field data-invalid={!!errors.email}>
            <FieldLabel htmlFor="edit-email">Email</FieldLabel>
            <Input
              id="edit-email"
              type="email"
              value={email}
              autoComplete="off"
              onChange={(event) => setEmail(event.target.value)}
            />
            {errors.email && <FieldError errors={[{ message: errors.email }]} />}
          </Field>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              className="cursor-pointer"
              disabled={submitting}
              onClick={onClose}
            >
              Cancel
            </Button>
            <Button type="submit" className="cursor-pointer" disabled={submitting}>
              {submitting ? "Saving..." : "Save changes"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}

function ResetPasswordDialog({
  member,
  onClose,
}: {
  member: Member
  onClose: () => void
}) {
  const { actions } = useFlowBoard()
  const [password, setPassword] = useState("")
  const [confirmPassword, setConfirmPassword] = useState("")
  const [error, setError] = useState("")
  const [submitting, setSubmitting] = useState(false)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (submitting) return
    const result = adminPasswordResetSchema.safeParse({ newPassword: password })
    if (!result.success) {
      setError(result.error.issues[0].message)
      return
    }
    if (password !== confirmPassword) {
      setError("The two passwords do not match.")
      return
    }
    setError("")
    setSubmitting(true)
    try {
      await actions.resetMemberPassword(member.id, password)
      toast.add({
        type: "success",
        title: "Password reset",
        description: `${member.name} must sign in with the new password and was signed out of all devices.`,
      })
      onClose()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Please try again.")
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Dialog open onOpenChange={(open) => !open && !submitting && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Reset password for {member.name}</DialogTitle>
          <DialogDescription>
            The new password is stored as a salted hash — it is never shown
            again. {member.name} will be signed out of every device and must
            sign in with it. Share it with them privately.
          </DialogDescription>
        </DialogHeader>
        <form onSubmit={handleSubmit} className="grid gap-4">
          <Field data-invalid={!!error}>
            <FieldLabel htmlFor="reset-new-password">New password</FieldLabel>
            <Input
              id="reset-new-password"
              type="password"
              value={password}
              autoComplete="new-password"
              placeholder="At least 8 characters, one uppercase letter, one number"
              onChange={(event) => setPassword(event.target.value)}
            />
          </Field>
          <Field data-invalid={!!error}>
            <FieldLabel htmlFor="reset-confirm-password">
              Confirm password
            </FieldLabel>
            <Input
              id="reset-confirm-password"
              type="password"
              value={confirmPassword}
              autoComplete="new-password"
              onChange={(event) => setConfirmPassword(event.target.value)}
            />
            {error && <FieldError errors={[{ message: error }]} />}
          </Field>
          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              className="cursor-pointer"
              disabled={submitting}
              onClick={onClose}
            >
              Cancel
            </Button>
            <Button type="submit" className="cursor-pointer" disabled={submitting}>
              {submitting ? "Resetting..." : "Reset password"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
