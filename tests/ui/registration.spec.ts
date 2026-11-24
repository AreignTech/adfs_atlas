import { expect, test } from "@playwright/test"

for (const width of [390, 768, 1440]) {
  test(`registration navigation and validation at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 })
    const errors: string[] = []
    page.on("pageerror", error => errors.push(error.message))
    await page.goto("/landing")
    await page.getByRole("button", { name: "Create an Account", exact: true }).click()
    await expect(page).toHaveURL(/\/sign-up$/)
    await page.getByRole("button", { name: "Create Account", exact: true }).click()
    await expect(page.getByText("Enter your first name.", { exact: true })).toBeVisible()
    await expect(page.getByText("Enter your last name.", { exact: true })).toBeVisible()
    await page.getByLabel("First Name").fill("New")
    await page.getByLabel("Last Name").fill("User")
    await page.getByLabel("Email Address").fill("invalid")
    await page.getByLabel("Password *", { exact: true }).fill("Password123!")
    await page.getByLabel("Confirm Password").fill("different")
    await page.getByRole("button", { name: "Create Account", exact: true }).click()
    await expect(page.getByText("Enter a valid email address.", { exact: true })).toBeVisible()
    await expect(page.getByText("Passwords do not match.", { exact: true })).toBeVisible()
    await page.getByLabel("Show passwords").check()
    await expect(page.getByLabel("Password *", { exact: true })).toHaveAttribute("type", "text")
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.getByRole("link", { name: "Log In", exact: true }).click()
    await expect(page).toHaveURL(/\/sign-in$/)
    await page.getByRole("link", { name: "Sign Up", exact: true }).click()
    await expect(page).toHaveURL(/\/sign-up$/)
    expect(errors).toEqual([])
  })
}

test("registration handles duplicate email and submits the existing API contract", async ({ page }) => {
  await page.route("**/api/atlas/auth/csrf", route => route.fulfill({ json: { token: "test-csrf" } }))
  let calls = 0
  await page.route("**/api/atlas/auth/register", async route => {
    calls++
    expect(route.request().postDataJSON()).toEqual({ name: "New User", email: "new@example.com", password: "Password123!" })
    expect(route.request().headers()["x-atlas-csrf"]).toBe("test-csrf")
    await route.fulfill(calls === 1 ? { status: 409, json: { detail: "Duplicate email" } } : { json: { user: {} } })
  })
  await page.goto("/sign-up")
  await page.getByLabel("First Name").fill("New")
  await page.getByLabel("Last Name").fill("User")
  await page.getByLabel("Email Address").fill("new@example.com")
  await page.getByLabel("Password *", { exact: true }).fill("Password123!")
  await page.getByLabel("Confirm Password").fill("Password123!")
  await page.getByRole("button", { name: "Create Account", exact: true }).click()
  await expect(page.getByText("An account already exists for that email address.")).toBeVisible()
  await page.getByRole("button", { name: "Create Account", exact: true }).click()
  await expect(page.getByRole("status")).toHaveText("Registration successful. You are signed in.")
  expect(calls).toBe(2)
})

test("invitation registration sends the private fragment token to the API", async ({ page }) => {
  await page.route("**/api/atlas/auth/csrf", route => route.fulfill({ json: { token: "test-csrf" } }))
  await page.route("**/api/atlas/auth/register", async route => {
    expect(route.request().postDataJSON()).toEqual({
      name: "Invited User", email: "invited@example.com", password: "Password123!", invitationToken: "private-invitation-token",
    })
    await route.fulfill({ json: { user: {} } })
  })
  await page.goto("/sign-up#invitation=private-invitation-token")
  await page.getByLabel("First Name").fill("Invited")
  await page.getByLabel("Last Name").fill("User")
  await page.getByLabel("Email Address").fill("invited@example.com")
  await page.getByLabel("Password *", { exact: true }).fill("Password123!")
  await page.getByLabel("Confirm Password").fill("Password123!")
  await page.getByRole("button", { name: "Create Account", exact: true }).click()
  await expect(page.getByRole("status")).toHaveText("Registration successful. You are signed in.")
})
