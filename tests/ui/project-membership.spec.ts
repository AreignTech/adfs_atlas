import { expect, test } from "@playwright/test"

test("a project teammate from another lab sees the project and tasks, and removal revokes access", async ({ page, browser, baseURL }) => {
  await page.goto("/sign-in")
  const email = process.env.ATLAS_UI_TEST_EMAIL ?? "nathan@aol.com"
  await page.getByLabel("Email").fill(email)
  await page.getByLabel("Password").fill(process.env.ATLAS_UI_TEST_PASSWORD ?? "P@ssword1")
  await page.getByRole("button", { name: "Login", exact: true }).click()
  await expect(page).toHaveURL(/\/dashboard$/)
  const token = (await (await page.request.get("/api/atlas/auth/csrf")).json()).token
  const headers = { "X-ATLAS-CSRF": token }
  const labs = await (await page.request.get("/api/atlas/admin/labs")).json()
  const sections = await (await page.request.get("/api/atlas/admin/sections")).json()
  expect(labs.length).toBeGreaterThan(1)
  const roles = await (await page.request.get("/api/atlas/admin/roles")).json()
  const workspace = await (await page.request.get("/api/atlas/workspace")).json()
  const owner = workspace.members.find((member: { email: string }) => member.email === email)
  const invitedEmail = `shared-project-${Date.now()}@atlas.test`
  const memberResponse = await page.request.post("/api/atlas/admin/members", { headers, data: {
    name: "Shared Project Teammate", email: invitedEmail, roleId: roles.find((r: { name: string }) => r.name === "Users").id,
    labId: labs[1].id, sectionId: sections[0].id,
  } })
  expect(memberResponse.ok(), await memberResponse.text()).toBeTruthy()
  const member = await memberResponse.json()
  const guest = await browser.newContext({ baseURL })
  let projectId: string | undefined
  let createdByTeammateId: string | undefined
  try {
    const invitation = await page.request.post(`/api/atlas/admin/members/${member.id}/invitation`, { headers })
    expect(invitation.ok()).toBeTruthy()
    const invitationToken = (await invitation.json()).token
    const guestCsrf = (await (await guest.request.get("/api/atlas/auth/csrf")).json()).token
    const registered = await guest.request.post("/api/atlas/auth/register", { headers: { "X-ATLAS-CSRF": guestCsrf }, data: {
      name: "Shared Project Teammate", email: invitedEmail, password: "SharedProjectTest123!", invitationToken,
    } })
    expect(registered.ok(), await registered.text()).toBeTruthy()
    const key = Array.from(crypto.getRandomValues(new Uint8Array(4)), (n) => String.fromCharCode(65 + n % 26)).join("")
    const projectName = `Shared project ${key}`
    const projectResponse = await page.request.post("/api/atlas/projects", { headers, data: {
      name: projectName, key, status: "active", dueDate: "2027-12-01", color: "var(--chart-1)",
      labId: labs[0].id, sectionId: sections[0].id, memberIds: [owner.id],
    } })
    expect(projectResponse.ok(), await projectResponse.text()).toBeTruthy()
    const project = await projectResponse.json()
    projectId = project.id
    const taskResponse = await page.request.post("/api/atlas/tasks", { headers, data: {
      projectId, title: `Shared task ${key}`, status: "todo", priority: "medium", dueDate: "2027-12-01", assigneeId: owner.id,
    } })
    expect(taskResponse.ok()).toBeTruthy()
    const task = await taskResponse.json()
    const recipient = await guest.newPage()
    await recipient.goto("/dashboard")
    await expect(recipient.getByRole("link", { name: projectName, exact: true })).toHaveCount(0)

    // Add the member using the real project team editor.
    await page.goto(`/projects/${projectId}`)
    await page.getByRole("button", { name: "Edit project" }).click()
    const team = page.getByRole("region", { name: "Project team", exact: true })
    await expect(team.getByText("Loading teammates…")).toHaveCount(0)
    await team.getByLabel("Search teammates").fill(invitedEmail)
    await team.getByRole("combobox", { name: "Lab Code", exact: true }).click()
    await page.getByRole("option", { name: `${labs[1].code} - ${labs[1].name}`, exact: true }).click()
    await team.getByRole("combobox", { name: "Section Code", exact: true }).click()
    await page.getByRole("option", { name: `${sections[0].code} - ${sections[0].name}`, exact: true }).click()
    await team.getByRole("button", { name: "Shared Project Teammate", exact: true }).click()
    await expect(team.getByRole("button", { name: "Shared Project Teammate", exact: true })).toHaveAttribute("aria-pressed", "true")
    await team.getByRole("combobox", { name: "Lab Code", exact: true }).click()
    await page.getByRole("option", { name: `${labs[0].code} - ${labs[0].name}`, exact: true }).click()
    await expect(team.getByRole("button", { name: "Shared Project Teammate", exact: true })).toHaveCount(0)
    await expect(team.getByRole("button", { name: "Remove Shared Project Teammate", exact: true })).toBeVisible()
    await expect(team.getByText("No teammates match these filters.")).toBeVisible()
    await team.getByRole("button", { name: "Clear filters", exact: true }).click()
    await expect(team.getByRole("combobox", { name: "Lab Code", exact: true })).toContainText("All labs")
    await expect(team.getByRole("combobox", { name: "Section Code", exact: true })).toContainText("All sections")
    await expect(team.getByRole("button", { name: "Shared Project Teammate", exact: true })).toHaveAttribute("aria-pressed", "true")
    const teamSaved = page.waitForResponse((response) => response.url().endsWith(`/api/atlas/projects/${projectId}`) && response.request().method() === "PATCH")
    await page.getByRole("button", { name: "Save changes", exact: true }).click()
    expect((await teamSaved).ok()).toBeTruthy()
    await expect(page.getByRole("dialog", { name: `Edit ${projectName}`, exact: true })).not.toBeVisible()
    await recipient.reload()
    await expect(recipient.getByRole("link", { name: projectName, exact: true })).toBeVisible()
    await expect(recipient.getByRole("link", { name: task.title, exact: true })).toBeVisible()
    await recipient.getByRole("link", { name: projectName, exact: true }).click()
    await expect(recipient.getByTestId(`task-${task.id}`)).toBeVisible()
    await recipient.getByTestId(`task-${task.id}`).click()
    await expect(recipient.getByRole("region", { name: "Task comments" }).getByText("No comments yet.")).toBeVisible()
    expect((await guest.request.get(`/api/atlas/projects/${projectId}`)).ok()).toBeTruthy()

    // A regular user can search the full directory and create a project without project codes.
    await recipient.getByRole("dialog", { name: task.title, exact: true }).press("Escape")
    await recipient.getByRole("button", { name: "New project", exact: true }).first().click()
    const createDialog = recipient.getByRole("dialog", { name: "New project", exact: true })
    const newKey = Array.from(crypto.getRandomValues(new Uint8Array(4)), (n) => String.fromCharCode(65 + n % 26)).join("")
    await createDialog.getByLabel("Project name", { exact: true }).fill(`Directory project ${newKey}`)
    await createDialog.getByLabel("Key", { exact: true }).fill(newKey)
    const guestTeam = createDialog.getByRole("region", { name: "Project team", exact: true })
    await expect(guestTeam.getByText("Loading teammates…")).toHaveCount(0)
    await guestTeam.getByLabel("Search teammates").fill(owner.email)
    await guestTeam.getByRole("button", { name: owner.name, exact: true }).click()
    const createResponse = recipient.waitForResponse((response) => response.url().endsWith("/api/atlas/projects") && response.request().method() === "POST")
    await createDialog.getByRole("button", { name: "Create project", exact: true }).click()
    const saved = await createResponse
    expect(saved.ok(), await saved.text()).toBeTruthy()
    const guestProject = await saved.json()
    createdByTeammateId = guestProject.id
    expect(guestProject.labId).toBeNull()
    expect(guestProject.sectionId).toBeNull()
    expect(guestProject.memberIds).toContain(owner.id)
    expect(guestProject.memberIds).toContain(member.id)

    const removed = await page.request.patch(`/api/atlas/projects/${projectId}`, { headers, data: { memberIds: [owner.id] } })
    expect(removed.ok(), await removed.text()).toBeTruthy()
    await recipient.goto("/dashboard")
    await expect(recipient.getByRole("link", { name: projectName, exact: true })).toHaveCount(0)
    await expect(recipient.getByRole("link", { name: task.title, exact: true })).toHaveCount(0)
    expect((await guest.request.get(`/api/atlas/projects/${projectId}`)).status()).toBe(403)
    expect((await guest.request.get(`/api/atlas/tasks/${task.id}/comments`)).status()).toBe(403)
  } finally {
    await guest.close()
    const cleanupToken = (await (await page.request.get("/api/atlas/auth/csrf")).json()).token
    const cleanupHeaders = { "X-ATLAS-CSRF": cleanupToken }
    if (createdByTeammateId) expect((await page.request.delete(`/api/atlas/projects/${createdByTeammateId}`, { headers: cleanupHeaders })).ok()).toBeTruthy()
    if (projectId) expect((await page.request.delete(`/api/atlas/projects/${projectId}`, { headers: cleanupHeaders })).ok()).toBeTruthy()
    expect((await page.request.delete(`/api/atlas/admin/members/${member.id}`, { headers: cleanupHeaders })).ok()).toBeTruthy()
  }
})
