import { expect, test, type Page } from "@playwright/test"

async function setup(page: Page) {
  await page.goto("/sign-in")
  const email = process.env.ATLAS_UI_TEST_EMAIL ?? "nathan@aol.com"
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Password").fill(process.env.ATLAS_UI_TEST_PASSWORD ?? "P@ssword1")
  await page.getByRole("button", { name: "Login", exact: true }).click()
  await expect(page).toHaveURL(/\/dashboard$/)
  const csrf = (await (await page.request.get("/api/atlas/auth/csrf")).json()).token
  const headers = { "X-ATLAS-CSRF": csrf }
  const labs = await (await page.request.get("/api/atlas/admin/labs")).json()
  const sections = await (await page.request.get("/api/atlas/admin/sections")).json()
  const workspace = await (await page.request.get("/api/atlas/workspace")).json()
  const member = workspace.members.find((item: { email: string }) => item.email === email)
  const key = Array.from(crypto.getRandomValues(new Uint8Array(4)), (n) => String.fromCharCode(65 + n % 26)).join("")
  const projectResponse = await page.request.post("/api/atlas/projects", { headers, data: {
    name: `Comments browser ${Date.now()}`, key, status: "active", dueDate: "2027-12-01",
    color: "var(--chart-1)", memberIds: [member.id], labId: labs[0].id, sectionId: sections[0].id,
  } })
  expect(projectResponse.ok(), await projectResponse.text()).toBeTruthy()
  const project = await projectResponse.json()
  const taskResponse = await page.request.post("/api/atlas/tasks", { headers, data: {
    projectId: project.id, title: "Comment browser task", status: "todo", priority: "medium", dueDate: "2027-12-01",
  } })
  expect(taskResponse.ok(), await taskResponse.text()).toBeTruthy()
  const task = await taskResponse.json()
  return { project, task, headers, async cleanup() {
    const token = (await (await page.request.get("/api/atlas/auth/csrf")).json()).token
    const response = await page.request.delete(`/api/atlas/projects/${project.id}`, { headers: { "X-ATLAS-CSRF": token } })
    expect(response.ok(), await response.text()).toBeTruthy()
  } }
}

test("task comments load on demand, persist, edit, confirm deletion and update board counts", async ({ page }) => {
  const fixture = await setup(page)
  try {
    let commentReads = 0
    page.on("request", (request) => { if (request.method() === "GET" && request.url().includes(`/tasks/${fixture.task.id}/comments`)) commentReads++ })
    await page.goto(`/projects/${fixture.project.id}`)
    const card = page.getByTestId(`task-${fixture.task.id}`)
    await expect(card.getByLabel("0 comments")).toBeVisible()
    expect(commentReads).toBe(0)
    await card.click()
    const comments = page.getByRole("region", { name: "Task comments" })
    await expect(comments.getByText("No comments yet.")).toBeVisible()
    const input = comments.getByLabel("Write a comment")
    const add = comments.getByRole("button", { name: "Add Comment", exact: true })
    await expect(add).toBeDisabled()
    await input.fill("  \n\t ")
    await expect(add).toBeDisabled()
    const content = "Browser comment <script>alert('text only')</script>"
    await input.fill(content)
    await add.click()
    await expect(comments.getByText(content, { exact: true })).toBeVisible()
    await expect(comments.getByRole("heading", { name: "Comments (1)", exact: true })).toBeVisible()
    await page.getByRole("dialog").press("Escape")
    await expect(card.getByLabel("1 comments")).toBeVisible()
    await page.reload()
    await card.click()
    await expect(comments.getByText(content, { exact: true })).toBeVisible()
    await comments.getByRole("button", { name: "Edit", exact: true }).click()
    await comments.getByLabel("Edit comment", { exact: true }).fill("Edited browser comment")
    await comments.getByRole("button", { name: "Save comment", exact: true }).click()
    await expect(comments.getByText("Edited browser comment", { exact: true })).toBeVisible()
    await expect(comments.getByText("(edited)", { exact: true })).toBeVisible()
    await comments.getByRole("button", { name: "Delete", exact: true }).click()
    await expect(comments.getByText("Permanently delete this comment?")).toBeVisible()
    await comments.getByRole("button", { name: "Cancel", exact: true }).click()
    await expect(comments.getByText("Edited browser comment", { exact: true })).toBeVisible()
    await comments.getByRole("button", { name: "Delete", exact: true }).click()
    await comments.getByRole("button", { name: "Delete comment", exact: true }).click()
    await expect(comments.getByText("No comments yet.")).toBeVisible()
    await page.getByRole("dialog").press("Escape")
    await expect(card.getByLabel("0 comments")).toBeVisible()
  } finally { await fixture.cleanup() }
})

test("comments show loading and errors, retain failed drafts, and fit a mobile dialog", async ({ page }) => {
  const fixture = await setup(page)
  try {
    await page.setViewportSize({ width: 390, height: 844 })
    let release!: () => void
    const pending = new Promise<void>((resolve) => { release = resolve })
    const url = `**/api/atlas/tasks/${fixture.task.id}/comments?*`
    await page.route(url, async (route) => {
      await pending
      await route.fulfill({ status: 503, contentType: "application/problem+json", body: JSON.stringify({ detail: "Comments temporarily unavailable." }) })
    })
    await page.goto(`/projects/${fixture.project.id}`)
    await page.getByTestId(`task-${fixture.task.id}`).click()
    const comments = page.getByRole("region", { name: "Task comments" })
    await expect(comments.getByRole("status")).toHaveText("Loading comments…")
    release()
    await expect(comments.getByRole("alert")).toContainText("Comments temporarily unavailable.")
    await page.unroute(url)
    await comments.getByRole("button", { name: "Retry loading comments" }).click()
    await expect(comments.getByText("No comments yet.")).toBeVisible()
    await page.route(`**/api/atlas/tasks/${fixture.task.id}/comments`, async (route) => {
      if (route.request().method() === "POST") await route.fulfill({ status: 503, contentType: "application/problem+json", body: JSON.stringify({ detail: "Could not save comment." }) })
      else await route.continue()
    }, { times: 1 })
    await comments.getByLabel("Write a comment").fill("Keep my draft")
    await comments.getByRole("button", { name: "Add Comment", exact: true }).click()
    await expect(comments.getByRole("alert")).toContainText("Could not save comment.")
    await expect(comments.getByLabel("Write a comment")).toHaveValue("Keep my draft")
    const box = await page.getByRole("dialog").boundingBox()
    expect(box!.width).toBeLessThanOrEqual(390)
    await comments.getByRole("button", { name: "Add Comment", exact: true }).click()
    await expect(comments.getByText("Keep my draft", { exact: true })).toBeVisible()
  } finally { await fixture.cleanup() }
})

test("comments paginate without loading every comment into the task dialog", async ({ page }) => {
  const fixture = await setup(page)
  try {
    for (let index = 0; index < 21; index++) {
      const response = await page.request.post(`/api/atlas/tasks/${fixture.task.id}/comments`, { headers: fixture.headers, data: { content: `Comment ${index}` } })
      expect(response.ok()).toBeTruthy()
    }
    await page.goto(`/projects/${fixture.project.id}`)
    await expect(page.getByTestId(`task-${fixture.task.id}`).getByLabel("21 comments")).toBeVisible()
    await page.getByTestId(`task-${fixture.task.id}`).click()
    const comments = page.getByRole("region", { name: "Task comments" })
    await expect(comments.getByRole("article")).toHaveCount(20)
    await expect(comments.getByText("Comment 0", { exact: true })).toHaveCount(0)
    await comments.getByRole("button", { name: "Load older comments" }).click()
    await expect(comments.getByRole("article")).toHaveCount(21)
    await expect(comments.getByText("Comment 0", { exact: true })).toBeVisible()
    await expect(comments.getByRole("button", { name: "Load older comments" })).toHaveCount(0)
  } finally { await fixture.cleanup() }
})
