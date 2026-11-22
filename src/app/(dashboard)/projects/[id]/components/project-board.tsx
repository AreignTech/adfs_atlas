"use client"

import * as React from "react"
import Link from "next/link"
import { useParams } from "next/navigation"
import {
  closestCorners,
  DndContext,
  DragOverlay,
  KeyboardSensor,
  PointerSensor,
  TouchSensor,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
  type DragOverEvent,
  type DragStartEvent,
} from "@dnd-kit/core"
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable"
import { CSS } from "@dnd-kit/utilities"
import {
  ArrowLeft,
  FolderOpen,
  ListTodo,
  SquareKanban,
  TriangleAlert,
} from "lucide-react"

import { Avatar, AvatarFallback, AvatarGroup } from "@/components/ui/avatar"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Progress } from "@/components/ui/progress"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"
import { TaskDetailDialog } from "@/components/task-detail-dialog"
import { toast } from "@/components/ui/toast"
import { AddTaskModal } from "@/app/(dashboard)/tasks/components/add-task-modal"
import { ProjectDialog } from "@/components/project-dialog"
import { cn } from "@/lib/utils"
import { useFlowBoard } from "@/data/flowboard-store"
import {
  formatDate,
  isOverdue,
  priorityClasses,
  priorityIcons,
  projectStatusClasses,
  projectStatuses,
  taskStatuses,
  type Task,
} from "@/data/flowboard"

export function ProjectBoard() {
  const params = useParams<{ id: string }>()
  const projectId = params?.id

  const {
    projectById,
    memberById,
    tasksForProject,
    projectProgress,
    actions,
  } = useFlowBoard()

  const [openTaskId, setOpenTaskId] = React.useState<string | null>(null)
  const [editOpen, setEditOpen] = React.useState(false)
  const [boardTasks, setBoardTasks] = React.useState(
    projectId ? tasksForProject(projectId) : []
  )
  const [activeTaskId, setActiveTaskId] = React.useState<string | null>(null)
  const dragSnapshot = React.useRef<typeof boardTasks>([])
  const suppressCardClick = React.useRef(false)

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(TouchSensor, {
      activationConstraint: { delay: 180, tolerance: 8 },
    }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  )

  React.useEffect(() => {
    if (!activeTaskId) {
      setBoardTasks(projectId ? tasksForProject(projectId) : [])
    }
  }, [projectId, tasksForProject, activeTaskId])

  const project = projectId ? projectById(projectId) : undefined

  if (!project) {
    return (
      <Card>
        <CardContent className="flex flex-col items-center gap-3 py-20 text-center">
          <FolderOpen className="size-8 text-muted-foreground" />
          <h1 className="text-xl font-semibold">Project not found</h1>
          <p className="max-w-md text-sm text-muted-foreground">
            This project does not exist in the workspace, or it was deleted.
          </p>
          <Button
            className="mt-2 cursor-pointer"
            nativeButton={false}
            render={<Link href="/projects" />}
          >
            Back to projects
          </Button>
        </CardContent>
      </Card>
    )
  }

  const status = projectStatuses.find((entry) => entry.value === project.status)!
  const { completed, total, progress } = projectProgress(project.id)
  const projectTasks = tasksForProject(project.id)
  const boardProjectId = project.id
  const boardProjectTasks = boardTasks.filter(
    (task) => task.projectId === boardProjectId
  )
  const remaining = Math.round(
    (new Date(project.dueDate).getTime() - startOfToday()) / 86_400_000
  )

  function handleDragStart(event: DragStartEvent) {
    const taskId = String(event.active.id)
    setActiveTaskId(taskId)
    dragSnapshot.current = boardProjectTasks.map((task) => ({ ...task }))
    suppressCardClick.current = true
  }

  function handleDragOver(event: DragOverEvent) {
    const activeId = String(event.active.id)
    const overId = event.over ? String(event.over.id) : null
    if (!overId) return

    setBoardTasks((current) => {
      const activeTask = current.find((task) => task.id === activeId)
      if (!activeTask) return current

      const overTask = current.find((task) => task.id === overId)
      const destinationStatus = overTask?.status ?? (
        taskStatuses.find((status) => status.value === overId)?.value
      )
      if (!destinationStatus) return current

      if (activeTask.status === destinationStatus) {
        const columnTasks = current
          .filter(
            (task) =>
              task.projectId === activeTask.projectId &&
              task.status === destinationStatus
          )
          .sort((a, b) => a.position - b.position)
        const oldIndex = columnTasks.findIndex((task) => task.id === activeId)
        const newIndex = overTask
          ? columnTasks.findIndex((task) => task.id === overId)
          : columnTasks.length - 1
        if (oldIndex === -1 || newIndex === -1 || oldIndex === newIndex) {
          return current
        }
        const reordered = arrayMove(columnTasks, oldIndex, newIndex)
        return normalizeColumnTasks(current, destinationStatus, reordered)
      }

      const remaining = current.filter((task) => task.id !== activeId)
      const destinationTasks = remaining
        .filter(
          (task) =>
            task.projectId === activeTask.projectId &&
            task.status === destinationStatus
        )
        .sort((a, b) => a.position - b.position)
      const insertIndex = overTask
        ? destinationTasks.findIndex((task) => task.id === overId)
        : destinationTasks.length
      destinationTasks.splice(
        insertIndex < 0 ? destinationTasks.length : insertIndex,
        0,
        { ...activeTask, status: destinationStatus }
      )
      return normalizeColumnTasks(
        remaining,
        destinationStatus,
        destinationTasks
      )
    })
  }

  async function handleDragEnd(event: DragEndEvent) {
    setActiveTaskId(null)
    suppressCardClick.current = true
    window.setTimeout(() => {
      suppressCardClick.current = false
    }, 250)

    if (!event.over) {
      setBoardTasks(dragSnapshot.current)
      return
    }

    const activeId = String(event.active.id)
    const normalized = normalizeProjectTasks(
      boardTasks.filter((task) => task.projectId === boardProjectId)
    )
    setBoardTasks(normalized)

    const originalById = new Map(
      dragSnapshot.current.map((task) => [task.id, task])
    )
    const changedItems = normalized
      .filter((task) => {
        const original = originalById.get(task.id)
        return (
          original &&
          (original.status !== task.status ||
            original.position !== task.position)
        )
      })
      .map((task) => ({
        taskId: task.id,
        status: task.status,
        position: task.position,
      }))

    if (changedItems.length === 0) return

    try {
      await actions.reorderTasks(boardProjectId, changedItems)
    } catch (error) {
      setBoardTasks(dragSnapshot.current)
      toast.add({
        type: "error",
        title: "Could not save card order",
        description:
          error instanceof Error
            ? error.message
            : "The card was restored to its previous position.",
      })
    }
  }

  return (
    <>
      <div className="flex flex-col gap-6">
        <div className="px-4 lg:px-6">
          <Button
            variant="ghost"
            size="sm"
            className="-ml-2 cursor-pointer text-muted-foreground"
            nativeButton={false}
            render={<Link href="/projects" />}
          >
            <ArrowLeft data-icon="inline-start" />
            All projects
          </Button>
        </div>

        <Card className="@container/card">
          <CardHeader>
            <div className="flex flex-wrap items-center gap-2">
              <span
                className="size-3 shrink-0 rounded-full"
                style={{ backgroundColor: project.color }}
                aria-hidden="true"
              />
              <CardTitle className="text-xl">{project.name}</CardTitle>
              <Badge
                variant="outline"
                className={projectStatusClasses[project.status]}
              >
                <status.icon />
                {status.label}
              </Badge>
              <span className="font-mono text-xs text-muted-foreground">
                {project.key}
              </span>
            </div>
            <CardDescription className="max-w-2xl">
              {project.description || "No description yet."}
            </CardDescription>
            <CardAction>
              <div className="flex items-center gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  className="cursor-pointer"
                  onClick={() => setEditOpen(true)}
                >
                  Edit project
                </Button>
                <AddTaskModal defaultProjectId={project.id} />
              </div>
            </CardAction>
          </CardHeader>

          <CardContent>
            <div className="grid gap-4 sm:grid-cols-2 @3xl:grid-cols-4">
              <Metric
                label="Progress"
                value={`${progress}%`}
                detail={`${completed} of ${total} tasks complete`}
              >
                <Progress value={progress} className="mt-2" />
              </Metric>
              <Metric
                label="Target date"
                value={formatDate(project.dueDate)}
                detail={
                  project.status === "completed"
                    ? "Shipped"
                    : remaining < 0
                      ? `${Math.abs(remaining)} days overdue`
                      : `${remaining} days remaining`
                }
                tone={
                  remaining < 0 && project.status !== "completed"
                    ? "destructive"
                    : undefined
                }
              />
              <Metric
                label="Overdue tasks"
                value={String(projectTasks.filter(isOverdue).length)}
                detail="Past their due date"
              />
              <div className="flex flex-col gap-1">
                <span className="text-sm text-muted-foreground">Team</span>
                <AvatarGroup>
                  {project.memberIds.map((memberId) => {
                    const member = memberById(memberId)
                    if (!member) return null
                    return (
                      <Avatar key={memberId} className="size-8">
                        <AvatarFallback className="text-xs">
                          {member.initials}
                        </AvatarFallback>
                      </Avatar>
                    )
                  })}
                </AvatarGroup>
              </div>
            </div>
          </CardContent>
        </Card>

        <Tabs defaultValue="board" className="w-full">
          <TabsList>
            <TabsTrigger value="board" className="cursor-pointer">
              <SquareKanban /> Board
            </TabsTrigger>
            <TabsTrigger value="list" className="cursor-pointer">
              <ListTodo /> List
            </TabsTrigger>
          </TabsList>

          <TabsContent value="board" className="mt-4">
            <DndContext
              sensors={sensors}
              collisionDetection={closestCorners}
              onDragStart={handleDragStart}
              onDragOver={handleDragOver}
              onDragCancel={() => {
                setBoardTasks(dragSnapshot.current)
                setActiveTaskId(null)
                suppressCardClick.current = true
              }}
              onDragEnd={handleDragEnd}
            >
              <div className="grid gap-4 md:grid-cols-2 @4xl:grid-cols-4">
                {taskStatuses.map((column) => {
                  const columnTasks = boardTasks
                    .filter((task) => task.status === column.value)
                    .sort((a, b) => a.position - b.position)

                  return (
                    <KanbanColumn
                      key={column.value}
                      status={column.value}
                      label={column.label}
                      icon={column.icon}
                      tasks={columnTasks}
                      activeTaskId={activeTaskId}
                      suppressCardClick={suppressCardClick}
                      onOpenTask={setOpenTaskId}
                      memberById={memberById}
                    />
                  )
                })}
              </div>
              <DragOverlay>
                {activeTaskId
                  ? (() => {
                      const task = boardTasks.find(
                        (entry) => entry.id === activeTaskId
                      )
                      return task ? (
                        <BoardTaskPreview
                          task={task}
                          memberName={
                            memberById(task.assigneeId)?.name
                          }
                        />
                      ) : null
                    })()
                  : null}
              </DragOverlay>
            </DndContext>
          </TabsContent>

          <TabsContent value="list" className="mt-4">
            <Card>
              <CardHeader>
                <CardTitle>All project tasks</CardTitle>
                <CardDescription>
                  Sorted by due date. Select a row to see full details.
                </CardDescription>
              </CardHeader>
              <CardContent>
                <Table>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Task</TableHead>
                      <TableHead>Status</TableHead>
                      <TableHead>Priority</TableHead>
                      <TableHead>Assignee</TableHead>
                      <TableHead>Due</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {[...projectTasks]
                      .sort((a, b) => a.dueDate.localeCompare(b.dueDate))
                      .map((task) => {
                        const taskStatus = taskStatuses.find(
                          (entry) => entry.value === task.status
                        )!
                        const assignee = memberById(task.assigneeId)
                        const PriorityIcon = priorityIcons[task.priority]
                        const overdue = isOverdue(task)

                        return (
                          <TableRow
                            key={task.id}
                            className="cursor-pointer"
                            onClick={() => setOpenTaskId(task.id)}
                          >
                            <TableCell className="max-w-[260px]">
                              <span className="block truncate font-medium">
                                {task.title}
                              </span>
                            </TableCell>
                            <TableCell>
                              <span className="flex items-center gap-1.5 text-sm">
                                <taskStatus.icon className="size-3.5 text-muted-foreground" />
                                {taskStatus.label}
                              </span>
                            </TableCell>
                            <TableCell>
                              <Badge
                                variant="outline"
                                className={cn(
                                  "capitalize",
                                  priorityClasses[task.priority]
                                )}
                              >
                                <PriorityIcon />
                                {task.priority}
                              </Badge>
                            </TableCell>
                            <TableCell className="text-sm text-muted-foreground">
                              {assignee?.name ?? "Unassigned"}
                            </TableCell>
                            <TableCell
                              className={cn(
                                "text-sm tabular-nums",
                                overdue ? "text-destructive" : "text-muted-foreground"
                              )}
                            >
                              {formatDate(task.dueDate)}
                            </TableCell>
                          </TableRow>
                        )
                      })}
                  </TableBody>
                </Table>
              </CardContent>
            </Card>
          </TabsContent>
        </Tabs>
      </div>

      <TaskDetailDialog
        taskId={openTaskId}
        onOpenChange={(open) => !open && setOpenTaskId(null)}
      />

      <ProjectDialog
        open={editOpen}
        onOpenChange={setEditOpen}
        project={project}
      />
    </>
  )
}

function KanbanColumn({
 status,
 label,
 icon: StatusIcon,
 tasks,
 activeTaskId,
 suppressCardClick,
 onOpenTask,
 memberById,
}: {
 status: Task["status"]
 label: string
 icon: React.ComponentType<{ className?: string }>
 tasks: Task[]
 activeTaskId: string | null
 suppressCardClick: React.MutableRefObject<boolean>
 onOpenTask: (taskId: string) => void
 memberById: (memberId: string) => { name: string } | undefined
}) {
 const { setNodeRef, isOver } = useDroppable({
   id: status,
   data: { type: "column", status },
 })

 return (
   <Card
     ref={setNodeRef}
     size="sm"
     className={cn(
       "gap-3 bg-muted/30 transition-colors",
       isOver && activeTaskId && "ring-2 ring-primary/40 bg-primary/5"
     )}
   >
     <CardHeader className="flex-row items-center justify-between">
       <CardDescription className="flex items-center gap-1.5 text-xs font-medium">
         <StatusIcon className="size-3.5" />
         {label}
       </CardDescription>
       <Badge variant="secondary">{tasks.length}</Badge>
     </CardHeader>
     <CardContent className="flex min-h-24 flex-col gap-2">
       <SortableContext
         items={tasks.map((task) => task.id)}
         strategy={verticalListSortingStrategy}
       >
         {tasks.length === 0 ? (
           <p className="flex min-h-20 items-center justify-center rounded-md border border-dashed p-4 text-center text-xs text-muted-foreground">
             {activeTaskId ? "Drop card here" : "Nothing here yet"}
           </p>
         ) : (
           tasks.map((task) => (
             <SortableTaskCard
               key={task.id}
               task={task}
               assigneeName={memberById(task.assigneeId)?.name}
               activeTaskId={activeTaskId}
               suppressCardClick={suppressCardClick}
               onOpen={() => onOpenTask(task.id)}
             />
           ))
         )}
       </SortableContext>
     </CardContent>
   </Card>
 )
}

function SortableTaskCard({
 task,
 assigneeName,
 activeTaskId,
 suppressCardClick,
 onOpen,
}: {
 task: Task
 assigneeName: string | undefined
 activeTaskId: string | null
 suppressCardClick: React.MutableRefObject<boolean>
 onOpen: () => void
}) {
 const {
   attributes,
   listeners,
   setNodeRef,
   transform,
   transition,
   isDragging,
 } = useSortable({
   id: task.id,
   data: { type: "task", status: task.status },
 })
 const PriorityIcon = priorityIcons[task.priority]
 const overdue = isOverdue(task)

 return (
   <button
     ref={setNodeRef}
     type="button"
     {...attributes}
     {...listeners}
     onClick={() => {
       if (!suppressCardClick.current && !activeTaskId) onOpen()
     }}
     className={cn(
       "touch-none cursor-grab rounded-lg border bg-card p-3 text-left transition-[box-shadow,opacity,transform] hover:shadow-md active:cursor-grabbing focus-visible:outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50",
       isDragging && "opacity-40"
     )}
     style={{
       transform: CSS.Transform.toString(transform),
       transition,
     }}
   >
     <p className="text-sm font-medium leading-snug">{task.title}</p>
     <div className="mt-3 flex items-center justify-between gap-2">
       <Badge
         variant="outline"
         className={cn("capitalize", priorityClasses[task.priority])}
       >
         <PriorityIcon />
         {task.priority}
       </Badge>
       <span
         className={cn(
           "flex items-center gap-1 text-xs tabular-nums",
           overdue ? "text-destructive" : "text-muted-foreground"
         )}
       >
         {overdue && <TriangleAlert className="size-3" />}
         {formatDate(task.dueDate)}
       </span>
     </div>
     {assigneeName && (
       <div className="mt-3 flex items-center gap-2">
         <Avatar className="size-5">
           <AvatarFallback className="text-[9px]">
             {assigneeName
               .split(" ")
               .map((part) => part[0])
               .join("")
               .slice(0, 2)
               .toUpperCase()}
           </AvatarFallback>
         </Avatar>
         <span className="truncate text-xs text-muted-foreground">
           {assigneeName}
         </span>
       </div>
     )}
   </button>
 )
}

function BoardTaskPreview({
 task,
 memberName,
}: {
 task: Task
 memberName?: string
}) {
 const PriorityIcon = priorityIcons[task.priority]
 return (
   <div className="w-[280px] rotate-2 rounded-lg border bg-card p-3 text-left shadow-xl">
     <p className="text-sm font-medium leading-snug">{task.title}</p>
     <div className="mt-3 flex items-center justify-between gap-2">
       <Badge
         variant="outline"
         className={cn("capitalize", priorityClasses[task.priority])}
       >
         <PriorityIcon />
         {task.priority}
       </Badge>
       {memberName && (
         <span className="text-xs text-muted-foreground">{memberName}</span>
       )}
     </div>
   </div>
 )
}

function normalizeColumnTasks(
 current: Task[],
 status: Task["status"],
 reordered: Task[]
) {
 const projectId = reordered[0]?.projectId
 const positionById = new Map(
   reordered.map((task, position) => [task.id, position])
 )
 return current
   .filter(
     (task) =>
       task.status !== status ||
       (projectId !== undefined && task.projectId !== projectId)
   )
   .concat(
     reordered.map((task, position) => ({
       ...task,
       status,
       position: positionById.get(task.id) ?? position,
     }))
   )
}

function normalizeProjectTasks(tasks: Task[]) {
 const result = [...tasks]
 for (const status of taskStatuses) {
   const projectIds = new Set(
     result
       .filter((task) => task.status === status.value)
       .map((task) => task.projectId)
   )
   for (const projectId of projectIds) {
     const ordered = result
       .filter(
         (task) =>
           task.projectId === projectId && task.status === status.value
       )
       .sort((a, b) => a.position - b.position)
     const positions = new Map(
       ordered.map((task, position) => [task.id, position])
     )
     for (const task of result) {
       if (
         task.projectId === projectId &&
         task.status === status.value
       ) {
         task.position = positions.get(task.id) ?? task.position
       }
     }
   }
 }
 return result
}

function Metric({
  label,
  value,
  detail,
  tone,
  children,
}: {
  label: string
  value: string
  detail: string
  tone?: "destructive"
  children?: React.ReactNode
}) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-sm text-muted-foreground">{label}</span>
      <span
        className={cn(
          "text-2xl font-semibold tabular-nums",
          tone === "destructive" && "text-destructive"
        )}
      >
        {value}
      </span>
      <span className="text-xs text-muted-foreground">{detail}</span>
      {children}
    </div>
  )
}

function startOfToday() {
  const date = new Date()
  date.setHours(0, 0, 0, 0)
  return date.getTime()
}