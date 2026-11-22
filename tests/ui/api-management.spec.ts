import { expect, test } from "@playwright/test"

test.describe("administrator API Management", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/sign-in")
    await page.getByLabel("Email").fill(process.env.ATLAS_UI_TEST_EMAIL ?? "nathan@aol.com")
    await page.getByLabel("Password").fill(process.env.ATLAS_UI_TEST_PASSWORD ?? "P@ssword1")
    await page.getByRole("button", { name: "Login", exact: true }).click()
    await expect(page).toHaveURL(/\/dashboard$/, { timeout: 15000 })
  })

  test("real metrics, filtered records, details, exports and configuration", async ({ page }) => {
    const errors: string[] = []
    page.on("pageerror", error => errors.push(error.message))
    await page.goto("/settings/api-management")
    await expect(page.getByRole("heading", { name: "API Health and Statistics", exact: true })).toBeVisible()
    await expect(page.getByText("Total API Requests", { exact: true })).toBeVisible()
    await expect(page.locator("tbody tr").first()).toBeVisible()
    await page.screenshot({ path: "test-results/api-health-desktop.png", fullPage: true })
    await page.getByRole("link", { name: "Activity Logs", exact: true }).click()
    await expect(page.getByRole("heading", { name: "API Activity Logs", exact: true })).toBeVisible()
    await expect(page.locator("tbody tr").first()).toBeVisible()
    await page.locator("tbody tr").first().getByRole("button").click()
    await expect(page.getByRole("heading", { name: "Request details", exact: true })).toBeVisible()
    await expect(page.getByRole("button", { name: "Copy Correlation ID" })).toBeEnabled()
    await page.getByRole("combobox", { name: "Result", exact: true }).selectOption("failed")
    await expect(page.locator('p[role="alert"]')).toHaveCount(0)
    const exported = await page.request.get("/api/atlas/admin/api-management/export?format=json&result=failed")
    expect(exported.ok()).toBeTruthy()
    const records = await exported.json() as { status: number }[]
    expect(records.every(record => record.status >= 400)).toBeTruthy()
    await page.getByRole("link", { name: "Configuration", exact: true }).click()
    await expect(page.getByLabel("Slow-request threshold (ms)")).toBeVisible()
    await page.getByRole("button", { name: "Save configuration" }).click()
    await expect(page.getByRole("status")).toHaveText("Monitoring configuration saved and audited.")
    await page.setViewportSize({ width: 390, height: 844 })
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: "test-results/api-configuration-mobile.png", fullPage: true })
    expect(errors).toEqual([])
  })

  test("Scalar loads the protected generated specification", async ({ page }) => {
    const errors: string[] = []
    page.on("pageerror", error => errors.push(error.message))
    const document = page.waitForResponse(response => response.url().includes("/api-management/openapi/v1.json") && response.request().method() === "GET")
    await page.goto("/settings/api-management/documentation")
    const response = await document
    expect(response.ok()).toBeTruthy()
    await expect(page.getByText("ATLAS API", { exact: true }).first()).toBeVisible({ timeout: 30000 })
    await expect(page.getByText("Workspace", { exact: true }).first()).toBeVisible()
    await page.getByRole("button", { name: "Open Group - Auth", exact: true }).click()
    await page.getByRole("link", { name: /Csrf/ }).first().click()
    await page.getByRole("button", { name: /Test Request/ }).first().click()
    const executed = page.waitForResponse(result => result.url().endsWith("/api/atlas/auth/csrf"))
    await page.getByRole("button", { name: /^Send get request to/ }).click()
    expect((await executed).ok()).toBeTruthy()
    const yaml = await page.request.get("/api/atlas/admin/api-management/openapi/v1.yaml")
    expect(yaml.ok()).toBeTruthy()
    expect(await yaml.text()).toContain("openapi:")
    await page.getByRole("button", { name: "Close Client", exact: true }).click()
    await page.getByRole("link", { name: /Logout/ }).first().click()
    await page.getByRole("button", { name: "Test Request (post /auth/logout)", exact: true }).click()
    const logout = page.waitForResponse(result => result.url().endsWith("/api/atlas/auth/logout"))
    await page.getByRole("button", { name: /^Send post request to/ }).click()
    const loggedOut = await logout
    expect(loggedOut.status()).toBe(204)
    expect(Boolean(loggedOut.request().headers()["x-atlas-csrf"])).toBeTruthy()
    await page.screenshot({ path: "test-results/api-scalar-desktop.png", fullPage: true })
    expect((await page.request.get("/api/atlas/admin/api-management/openapi/v1.yaml")).status()).toBe(401)
    expect(errors).toEqual([])
  })
})

test("signed-out visitors cannot open API Management", async ({ page }) => {
  await page.goto("/settings/api-management")
  await expect(page).toHaveURL(/\/sign-in(?:\?|$)/)
  const response = await page.request.get("/api/atlas/admin/api-management/activity")
  expect(response.status()).toBe(401)
})
