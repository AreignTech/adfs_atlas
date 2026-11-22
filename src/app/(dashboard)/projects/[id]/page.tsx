import { ProjectBoard } from "./components/project-board"
import { headers } from "next/headers"
import { redirect, notFound } from "next/navigation"
import { eq } from "drizzle-orm"
import { auth } from "@/lib/auth"
import { db } from "@/db"
import { project } from "@/db/schema"
import { requireOrgScope } from "@/lib/org"

export const metadata = {
  title: "Project board",
  description: "Kanban board and task list for an ATLAS project.",
}

export default async function ProjectPage({
  params,
}: {
  params: Promise<{ id: string }>
}) {
  const { id } = await params
  const session = await auth.api.getSession({ headers: await headers() })
  if (!session) redirect("/sign-in")
  const [projectRow] = await db.select().from(project).where(eq(project.id, id)).limit(1)
  if (!projectRow) notFound()
  const authorized = await requireOrgScope(projectRow.labId, projectRow.sectionId)
  if (!authorized.ok) redirect("/projects")
  return <ProjectBoard />
}
