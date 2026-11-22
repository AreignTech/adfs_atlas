import { expect, test, type Page } from "@playwright/test"
import { projectSchema, taskSchema, workspaceSchema } from "../../src/lib/api/contracts"

async function login(page: Page) {
  await page.goto("/sign-in")
  await page.getByLabel("Email").fill(process.env.ATLAS_UI_TEST_EMAIL ?? "nathan@aol.com")
  await page.getByLabel("Password").fill(process.env.ATLAS_UI_TEST_PASSWORD ?? "P@ssword1")
  await page.getByRole("button", { name: "Login", exact: true }).click()
  await expect(page).toHaveURL(/\/dashboard$/)
}
async function write(page: Page, method: string, path: string, data?: unknown) {
  const csrf = await page.request.get("/api/atlas/auth/csrf")
  const token = (await csrf.json()).token as string
  const response = await page.request.fetch("/api/atlas" + path, { method, headers: { "X-ATLAS-CSRF": token }, ...(data !== undefined ? { data } : {}) })
  expect(response.ok(), await response.text()).toBeTruthy()
  return response.status() === 204 ? undefined : response.json()
}
test("project/task workflows and actual Kanban drag persist through the API", async ({ page }) => {
  await login(page)
  const workspace = workspaceSchema.parse(await (await page.request.get("/api/atlas/workspace")).json())
  const member = workspace.members.find(m => m.email === (process.env.ATLAS_UI_TEST_EMAIL ?? "nathan@aol.com"))!
  const labs = await (await page.request.get("/api/atlas/admin/labs")).json() as { id: string }[]
  const sections = await (await page.request.get("/api/atlas/admin/sections")).json() as { id: string }[]
  const key = Array.from({ length: 4 }, () => String.fromCharCode(65 + Math.floor(Math.random() * 26))).join("")
  const project = projectSchema.parse(await write(page, "POST", "/projects", { name: "Migration browser " + key, key, status: "active", dueDate: "2026-12-20", startDate: "2026-11-20", color: "var(--chart-1)", memberIds: [member.id], labId: labs[0].id, sectionId: sections[0].id }))
  try {
    const task = taskSchema.parse(await write(page, "POST", "/tasks", { projectId: project.id, title: "Drag migration task " + key, status: "todo", priority: "high", dueDate: "2026-12-15", assigneeId: member.id }))
    const second = taskSchema.parse(await write(page, "POST", "/tasks", { projectId: project.id, title: "Second migration task " + key, status: "todo", priority: "medium", dueDate: "2026-12-16", assigneeId: member.id }))
    await write(page, "POST", "/tasks/reorder", { projectId: project.id, items: [{ taskId: task.id, status: "todo", position: 0 }, { taskId: second.id, status: "todo", position: 1 }] })
    const ordered = workspaceSchema.parse(await (await page.request.get("/api/atlas/workspace")).json()).tasks.filter(t => t.projectId === project.id)
    expect(ordered.map(t => t.id)).toEqual([task.id, second.id])
    await page.goto("/projects/" + project.id)
    await expect(page.getByText(project.name, { exact: true })).toBeVisible()
    const card = page.getByTestId("task-" + task.id)
    await expect(card).toBeVisible()
    const from = (await card.boundingBox())!
    const to = (await page.getByTestId("column-in-progress").boundingBox())!
    const reordered = page.waitForResponse(r => r.url().endsWith("/api/atlas/tasks/reorder") && r.request().method() === "POST")
    await page.mouse.move(from.x + from.width / 2, from.y + from.height / 2)
    await page.mouse.down()
    await page.mouse.move(from.x + from.width / 2 + 12, from.y + from.height / 2, { steps: 3 })
    await page.mouse.move(to.x + to.width / 2, to.y + to.height / 2, { steps: 15 })
    await page.mouse.up()
    expect((await reordered).ok()).toBeTruthy()
    await page.reload()
    await expect(page.getByTestId("column-in-progress").getByTestId("task-" + task.id)).toBeVisible()
    const edited = taskSchema.parse(await write(page, "PATCH", "/tasks/" + task.id, { title: "Edited migration " + key, status: "completed" }))
    expect(edited.status).toBe("completed")
    const copied = taskSchema.parse(await write(page, "POST", "/tasks/" + task.id + "/duplicate", {}))
    expect(copied.id).not.toBe(task.id)
    await write(page, "DELETE", "/tasks/" + copied.id)
    for (const route of ["/calendar", "/users", "/settings/roles", "/settings/authentication", "/settings/audit"]) {
      await page.goto(route)
      await expect(page.locator("main")).toBeVisible()
      await expect(page.getByText("Application error", { exact: false })).toHaveCount(0)
      if (route === "/settings/roles") await expect(page.getByText("Administrators", { exact: true }).first()).toBeVisible()
      if (route === "/settings/authentication") await expect(page.getByText("LDAP Configuration", { exact: true })).toBeVisible()
      if (route === "/settings/audit") await expect(page.getByText("RBAC Activity", { exact: true })).toBeVisible()
    }
  } finally { await write(page, "DELETE", "/projects/" + project.id) }
})
test("organization administration and notification preferences persist", async ({ page }) => {
  await login(page)
  const code = "UI_" + Date.now()
  await page.goto("/settings/labs")
  await page.getByLabel("Lab Code", { exact: true }).fill(code)
  await page.getByLabel("Lab Name", { exact: true }).fill("Browser validation lab")
  await page.getByRole("button", { name: "Create Lab Code", exact: true }).click()
  await expect(page.getByRole("heading", { name: code })).toBeVisible()
  const labs = await (await page.request.get("/api/atlas/admin/labs")).json() as { id: string; code: string }[]
  try {
    await page.reload()
    await expect(page.getByRole("heading", { name: code })).toBeVisible()
    await page.goto("/settings/notifications")
    const save = page.waitForResponse(r => r.url().endsWith("/api/atlas/settings/notifications") && r.request().method() === "POST")
    await page.getByRole("button", { name: "Save preferences" }).click()
    expect((await save).ok()).toBeTruthy()
    const saved = await (await page.request.get("/api/atlas/settings/notifications")).json()
    expect(saved.value.matrix.taskAssigned.inApp).toBe(true)
  } finally { await write(page, "DELETE", "/admin/labs/" + labs.find(l => l.code === code)!.id) }
})
