import { expect, test } from "@playwright/test"

test("landing page links to sign in", async ({ page }) => {
  await page.goto("/landing")

  await expect(
    page.getByRole("heading", {
      name: "ATLAS",
      exact: true,
    })
  ).toBeVisible()
  await expect(
    page.getByText("Simple pricing that scales with your team")
  ).toHaveCount(0)
  await expect(page.getByRole("link", { name: "Pricing" })).toHaveCount(0)

  await page.getByRole("button", { name: "Sign In" }).first().click()
  await expect(page).toHaveURL(/\/sign-in$/)
})

test("sign-in form displays validation for an empty email", async ({ page }) => {
  await page.goto("/sign-in")

  await expect(page.getByText("Welcome back", { exact: true })).toBeVisible()
  await page.getByRole("button", { name: "Login", exact: true }).click()
  await expect(page.getByText("Invalid email address")).toBeVisible()
})

test("signed-out visitors are redirected from the dashboard to sign-in", async ({
  page,
}) => {
  await page.goto("/dashboard")

  await expect(page).toHaveURL(/\/sign-in$/)
})

test("demo account sign-in redirects to the dashboard", async ({ page }) => {
  await page.goto("/sign-in")
  await page.getByLabel("Email").fill(process.env.ATLAS_UI_TEST_EMAIL ?? "nathan@aol.com")
  await page.getByLabel("Password").fill(process.env.ATLAS_UI_TEST_PASSWORD ?? "P@ssword1")
  await page.getByRole("button", { name: "Login", exact: true }).click()
  await expect(page).toHaveURL(/\/dashboard$/, { timeout: 10_000 })
  await expect(page.getByText("In Review", { exact: true }).first()).toBeVisible()
  await page.goto("/tasks")
  await expect(page.getByText("In Review", { exact: true }).first()).toBeVisible()
})

test("an invalid session cookie redirects to sign-in instead of failing the layout", async ({ page, context, baseURL }) => {
  await context.addCookies([{ name: "atlas.session_token", value: "invalid-or-expired-session", url: baseURL!, httpOnly: true, sameSite: "Lax" }])
  await page.goto("/dashboard")
  await expect(page).toHaveURL(/\/sign-in$/)
  await expect(page.getByText("Welcome back", { exact: true })).toBeVisible()
})
