import { ProjectBoard } from "./components/project-board"
import { headers } from "next/headers"
import { redirect, notFound } from "next/navigation"
import { auth } from "@/lib/auth"
import { serverApi } from "@/lib/api/server"
import { ApiError } from "@/lib/api/client"

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
  try { await serverApi("/projects/" + id) }
  catch (error) {
    if (error instanceof ApiError && error.status === 404) notFound()
    if (error instanceof ApiError && error.status === 403) redirect("/projects")
    throw error
  }
  return <ProjectBoard />
}
