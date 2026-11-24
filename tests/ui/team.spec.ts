import { expect, test } from "@playwright/test"

test("team invitation link activates the existing member", async ({ page, browser }) => {
  await page.goto("/sign-in")
  await page.getByLabel("Email").fill(process.env.ATLAS_UI_TEST_EMAIL ?? "nathan@aol.com")
  await page.getByLabel("Password").fill(process.env.ATLAS_UI_TEST_PASSWORD ?? "P@ssword1")
  await page.getByRole("button", { name: "Login", exact: true }).click()
  await expect(page).toHaveURL(/\/dashboard$/)

  const email = `team-browser-${Date.now()}@atlas.test`
  const csrf = (await (await page.request.get("/api/atlas/auth/csrf")).json()).token
  const labs = await (await page.request.get("/api/atlas/admin/labs")).json()
  const sections = await (await page.request.get("/api/atlas/admin/sections")).json()
  const roles = await (await page.request.get("/api/atlas/admin/roles")).json()
  const created = await page.request.post("/api/atlas/admin/members", {
    headers: { "X-ATLAS-CSRF": csrf },
    data: { name: "Browser Invitee", email, roleId: roles.find((r: { name: string }) => r.name === "Users").id, labId: labs[0].id, sectionId: sections[0].id },
  })
  expect(created.ok(), await created.text()).toBeTruthy()
  const member = await created.json()
  const guest = await browser.newContext()
  try {
    await page.goto("/users")
    await page.getByPlaceholder(/search/i).fill(email)
    const row = page.getByRole("row").filter({ hasText: email })
    await expect(row.getByText("Invited", { exact: true })).toBeVisible()
    await row.getByRole("button", { name: "More actions for Browser Invitee" }).click()
    await page.getByRole("menuitem", { name: "Create new invitation link" }).click()
    const link = page.getByRole("textbox", { name: "Invitation link" })
    await expect(link).toHaveValue(/\/sign-up#invitation=.+/)

    const recipient = await guest.newPage()
    await recipient.goto(await link.inputValue())
    await recipient.getByLabel("First Name").fill("Browser")
    await recipient.getByLabel("Last Name").fill("Invitee")
    await recipient.getByLabel("Email Address").fill(email)
    await recipient.getByLabel("Password *", { exact: true }).fill("InvitationBrowser123!")
    await recipient.getByLabel("Confirm Password").fill("InvitationBrowser123!")
    await recipient.getByRole("button", { name: "Create Account", exact: true }).click()
    await expect(recipient.getByRole("status")).toHaveText("Registration successful. You are signed in.")
    await page.reload()
    await page.getByPlaceholder(/search/i).fill(email)
    await expect(row.getByText("Active", { exact: true })).toBeVisible()
    await expect(row.getByText("Never signed in", { exact: true })).toHaveCount(0)
  } finally {
    await guest.close()
    const cleanupCsrf = (await (await page.request.get("/api/atlas/auth/csrf")).json()).token
    const removed = await page.request.delete(`/api/atlas/admin/members/${member.id}`, { headers: { "X-ATLAS-CSRF": cleanupCsrf } })
    expect(removed.ok(), await removed.text()).toBeTruthy()
  }
})
