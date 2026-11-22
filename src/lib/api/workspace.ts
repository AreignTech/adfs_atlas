import "server-only"
import type { Member, Project, Task } from "@/data/flowboard"
import { serverApi } from "@/lib/api/server"
import { workspaceSchema } from "@/lib/api/contracts"
export interface WorkspaceData { projects: Project[]; tasks: Task[]; members: Member[] }
export async function getWorkspaceData(_scope?: { isAdministrator: boolean; labId: string | null; sectionId: string | null }): Promise<WorkspaceData> {
  void _scope
  return workspaceSchema.parse(await serverApi("/workspace"))
}
