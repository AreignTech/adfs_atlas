import { expect, test } from "@playwright/test"

const ADMIN_EMAIL = process.env.ATLAS_UI_TEST_EMAIL ?? "nathan@aol.com"
const ADMIN_PASSWORD = process.env.ATLAS_UI_TEST_PASSWORD ?? "P@ssword1"
const INVITE_PASSWORD = "InvitationAdmin123!"
const RESET_PASSWORD = "AdminReset99x"

test("administrator edits, resets password of, and deactivates a member", async ({
  page,
  browser,
}) => {
  await page.goto("/sign-in")
  await page.getByLabel("Email").fill(ADMIN_EMAIL)
  await page.getByLabel("Password").fill(ADMIN_PASSWORD)
  await page.getByRole("button", { name: "Login", exact: true }).click()
  await expect(page).toHaveURL(/\/dashboard$/)

  const email = `admin-mgmt-${Date.now()}@atlas.test`
  const csrf = (await (await page.request.get("/api/atlas/auth/csrf")).json()).token
  const labs = await (await page.request.get("/api/atlas/admin/labs")).json()
  const sections = await (
    await page.request.get("/api/atlas/admin/sections")
  ).json()
  const roles = await (await page.request.get("/api/atlas/admin/roles")).json()
  const created = await page.request.post("/api/atlas/admin/members", {
    headers: { "X-ATLAS-CSRF": csrf },
    data: {
      name: "Admin Mgmt",
      email,
      roleId: roles.find((r: { name: string }) => r.name === "Users").id,
      labId: labs[0].id,
      sectionId: sections[0].id,
    },
  })
  expect(created.ok(), await created.text()).toBeTruthy()
  const member = await created.json()

  const guest = await browser.newContext()
  try {
    // Accept the invitation so the member has a real credential.
    const invitation = await page.request.post(
      `/api/atlas/admin/members/${member.id}/invitation`,
      { headers: { "X-ATLAS-CSRF": csrf } }
    )
    expect(invitation.ok(), await invitation.text()).toBeTruthy()
    const { token } = await invitation.json()
    const recipient = await guest.newPage()
    await recipient.goto(`${new URL(page.url()).origin}/sign-up#invitation=${encodeURIComponent(token)}`)
    await recipient.getByLabel("First Name").fill("Admin")
    await recipient.getByLabel("Last Name").fill("Mgmt")
    await recipient.getByLabel("Email Address").fill(email)
    await recipient.getByLabel("Password *", { exact: true }).fill(INVITE_PASSWORD)
    await recipient.getByLabel("Confirm Password").fill(INVITE_PASSWORD)
    await recipient.getByRole("button", { name: "Create Account", exact: true }).click()
    await expect(recipient.getByRole("status")).toHaveText(
      "Registration successful. You are signed in."
    )

    await page.goto("/users")
    await page.getByPlaceholder(/search/i).fill(email)
    const row = page.getByRole("row").filter({ hasText: email })
    await expect(row.getByText("Active", { exact: true })).toBeVisible()

    // Edit details — duplicate email is rejected, valid edit persists.
    await row.getByRole("button", { name: "More actions for Admin Mgmt" }).click()
    await page.getByRole("menuitem", { name: "Edit details" }).click()
    await page.getByLabel("Email", { exact: true }).fill(ADMIN_EMAIL)
    await page.getByRole("button", { name: "Save changes" }).click()
    await expect(
      page.getByRole("alert").filter({ hasText: "already uses that email" })
    ).toBeVisible()
    await page.getByLabel("First name").fill("Renamed")
    await page.getByLabel("Email", { exact: true }).fill(email)
    await page.getByRole("button", { name: "Save changes" }).click()
    await expect(
      page
        .getByRole("row")
        .filter({ hasText: email })
        .getByText("Renamed Mgmt", { exact: true })
    ).toBeVisible()
    await page.reload()
    await page.getByPlaceholder(/search/i).fill(email)
    await expect(
      page
        .getByRole("row")
        .filter({ hasText: email })
        .getByText("Renamed Mgmt", { exact: true })
    ).toBeVisible()

    // Administrator password reset — old password stops working, new works.
    await row.getByRole("button", { name: "More actions for Renamed Mgmt" }).click()
    await page.getByRole("menuitem", { name: "Reset password" }).click()
    await page.getByLabel("New password").fill(RESET_PASSWORD)
    await page.getByLabel("Confirm password").fill(RESET_PASSWORD)
    await page.getByRole("button", { name: "Reset password" }).click()
    await expect(page.getByText("signed out of all devices")).toBeVisible()

    const afterReset = await browser.newContext()
    const afterResetPage = await afterReset.newPage()
    await afterResetPage.goto("/sign-in")
    await afterResetPage.getByLabel("Email").fill(email)
    await afterResetPage.getByLabel("Password").fill(INVITE_PASSWORD)
    await afterResetPage.getByRole("button", { name: "Login", exact: true }).click()
    await expect(
      afterResetPage.getByRole("alert").filter({ hasText: "Invalid email or password." })
    ).toBeVisible()
    await afterResetPage.getByLabel("Password").fill(RESET_PASSWORD)
    await afterResetPage.getByRole("button", { name: "Login", exact: true }).click()
    await expect(afterResetPage).toHaveURL(/\/dashboard$/)

    // The member is not an administrator: /users is gated server-side, so
    // direct navigation lands back on the dashboard with no team table.
    await afterResetPage.goto("/users")
    await expect(afterResetPage).toHaveURL(/\/dashboard$/)
    await expect(afterResetPage.getByPlaceholder(/search/i)).toHaveCount(0)

    // Deactivate — sign-in is blocked until an administrator reactivates.
    await page.bringToFront()
    await page.getByPlaceholder(/search/i).fill(email)
    await row.getByRole("button", { name: "More actions for Renamed Mgmt" }).click()
    await page.getByRole("menuitem", { name: "Deactivate account" }).click()
    await page.getByRole("button", { name: "Deactivate account" }).click()
    await expect(row.getByText("Inactive", { exact: true })).toBeVisible()

    const blocked = await browser.newContext()
    const blockedPage = await blocked.newPage()
    await blockedPage.goto("/sign-in")
    await blockedPage.getByLabel("Email").fill(email)
    await blockedPage.getByLabel("Password").fill(RESET_PASSWORD)
    await blockedPage.getByRole("button", { name: "Login", exact: true }).click()
    await expect(
      blockedPage.getByRole("alert").filter({ hasText: "deactivated" })
    ).toBeVisible()
    await blocked.close()

    // Reactivate — status returns to Active and sign-in works again.
    await page.bringToFront()
    await page.getByPlaceholder(/search/i).fill(email)
    await row.getByRole("button", { name: "More actions for Renamed Mgmt" }).click()
    await page.getByRole("menuitem", { name: "Activate account" }).click()
    await page.getByRole("button", { name: "Reactivate account" }).click()
    await expect(row.getByText("Active", { exact: true })).toBeVisible()

    const reactivated = await browser.newContext()
    const reactivatedPage = await reactivated.newPage()
    await reactivatedPage.goto("/sign-in")
    await reactivatedPage.getByLabel("Email").fill(email)
    await reactivatedPage.getByLabel("Password").fill(RESET_PASSWORD)
    await reactivatedPage.getByRole("button", { name: "Login", exact: true }).click()
    await expect(reactivatedPage).toHaveURL(/\/dashboard$/)
    await reactivated.close()
  } finally {
    await guest.close()
    const cleanupCsrf = (
      await (await page.request.get("/api/atlas/auth/csrf")).json()
    ).token
    const removed = await page.request.delete(
      `/api/atlas/admin/members/${member.id}`,
      { headers: { "X-ATLAS-CSRF": cleanupCsrf } }
    )
    expect(removed.ok(), await removed.text()).toBeTruthy()
  }
})
