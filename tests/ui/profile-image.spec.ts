import { expect, test } from "@playwright/test"

test("profile photos validate before upload, persist, support GIF, and reset correctly", async ({ page, browser, baseURL }) => {
  await page.goto("/sign-in")
  await page.getByLabel("Email").fill(process.env.ATLAS_UI_TEST_EMAIL ?? "nathan@aol.com")
  await page.getByLabel("Password").fill(process.env.ATLAS_UI_TEST_PASSWORD ?? "P@ssword1")
  await page.getByRole("button", { name: "Login", exact: true }).click()
  await expect(page).toHaveURL(/\/dashboard$/)
  const csrf = (await (await page.request.get("/api/atlas/auth/csrf")).json()).token
  const headers = { "X-ATLAS-CSRF": csrf }
  const labs = await (await page.request.get("/api/atlas/admin/labs")).json()
  const sections = await (await page.request.get("/api/atlas/admin/sections")).json()
  const roles = await (await page.request.get("/api/atlas/admin/roles")).json()
  const email = `profile-photo-${Date.now()}@atlas.test`
  const memberResponse = await page.request.post("/api/atlas/admin/members", { headers, data: {
    name: "Photo Browser", email, roleId: roles.find((role: { name: string }) => role.name === "Users").id,
    labId: labs[0].id, sectionId: sections[0].id,
  } })
  expect(memberResponse.ok()).toBeTruthy()
  const member = await memberResponse.json()
  const guest = await browser.newContext({ baseURL })
  try {
    const invitation = await (await page.request.post(`/api/atlas/admin/members/${member.id}/invitation`, { headers })).json()
    const guestToken = (await (await guest.request.get("/api/atlas/auth/csrf")).json()).token
    const registration = await guest.request.post("/api/atlas/auth/register", { headers: { "X-ATLAS-CSRF": guestToken }, data: {
      name: "Photo Browser", email, password: "PhotoBrowserTest123!", invitationToken: invitation.token,
    } })
    expect(registration.ok()).toBeTruthy()
    const recipient = await guest.newPage()
    await recipient.goto("/settings/user")
    const save = recipient.getByRole("button", { name: "Save changes", exact: true })
    await expect(save).toBeEnabled()
    const input = recipient.getByLabel("Profile photo upload", { exact: true })
    let posts = 0
    recipient.on("request", (request) => { if (request.method() === "POST" && request.url().endsWith("/settings/profile")) posts++ })
    await input.setInputFiles({ name: "large.png", mimeType: "image/png", buffer: Buffer.alloc(2 * 1024 * 1024 + 1) })
    await expect(recipient.getByRole("alert").filter({ hasText: "no larger than 2 MB" })).toBeVisible()
    expect(posts).toBe(0)
    await input.setInputFiles({ name: "document.svg", mimeType: "image/svg+xml", buffer: Buffer.from("<svg/>") })
    await expect(recipient.getByRole("alert").filter({ hasText: "Choose a JPG, PNG, GIF or WebP" })).toBeVisible()
    expect(posts).toBe(0)

    const png = Buffer.concat([Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jD1sAAAAASUVORK5CYII=", "base64"), Buffer.alloc(200_000)])
    const chooser = recipient.waitForEvent("filechooser")
    await recipient.getByRole("button", { name: "Upload new photo", exact: true }).click()
    await (await chooser).setFiles({ name: "photo.png", mimeType: "image/png", buffer: png })
    const photo = recipient.getByRole("img", { name: "Profile photo", exact: true })
    await expect(photo).toBeVisible()
    await expect(photo).toHaveJSProperty("naturalWidth", 1)
    expect(posts).toBe(0)

    async function saveProfile() {
      const response = recipient.waitForResponse((r) => r.url().endsWith("/api/atlas/settings/profile") && r.request().method() === "POST")
      await recipient.getByRole("button", { name: "Save changes", exact: true }).click()
      expect((await response).ok()).toBeTruthy()
      await expect(recipient.getByText("Profile updated", { exact: true })).toBeVisible()
    }
    await saveProfile()
    await recipient.reload()
    await expect(photo).toBeVisible()
    await expect(photo).toHaveJSProperty("naturalWidth", 1)
    const persisted = await (await guest.request.get("/api/atlas/settings/profile")).json()
    expect(persisted.user.image === `data:image/png;base64,${png.toString("base64")}`).toBeTruthy()

    const gif = Buffer.from("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7", "base64")
    await input.setInputFiles({ name: "photo.gif", mimeType: "image/gif", buffer: gif })
    await expect(photo).toHaveAttribute("src", /^data:image\/gif;base64,/)
    await recipient.getByRole("button", { name: "Cancel", exact: true }).click()
    await expect(photo).toHaveAttribute("src", /^data:image\/png;base64,/)
    await input.setInputFiles({ name: "photo.gif", mimeType: "image/gif", buffer: gif })
    await expect(photo).toHaveAttribute("src", /^data:image\/gif;base64,/)
    await saveProfile()
    await recipient.reload()
    await expect(photo).toHaveAttribute("src", /^data:image\/gif;base64,/)
    await expect(photo).toHaveJSProperty("naturalWidth", 1)
    await expect(recipient.getByRole("button", { name: "Reset", exact: true })).toBeEnabled()
    const postsBeforeReset = posts
    await recipient.getByRole("button", { name: "Reset", exact: true }).click()
    expect(posts).toBe(postsBeforeReset)
    await saveProfile()
    expect((await (await guest.request.get("/api/atlas/settings/profile")).json()).user.image).toBeNull()

    const uploadToken = (await (await guest.request.get("/api/atlas/auth/csrf")).json()).token
    const largeRequest = await guest.request.post("/api/atlas/settings/profile", { headers: { "X-ATLAS-CSRF": uploadToken }, data: {
      firstName: "Photo", lastName: "Browser", email, profileImage: "data:image/png;base64," + Buffer.alloc(2_300_000).toString("base64"),
    } })
    expect(largeRequest.status()).toBe(413)
    expect((await largeRequest.json()).detail).toContain("too large")
  } finally {
    await guest.close()
    const token = (await (await page.request.get("/api/atlas/auth/csrf")).json()).token
    expect((await page.request.delete(`/api/atlas/admin/members/${member.id}`, { headers: { "X-ATLAS-CSRF": token } })).ok()).toBeTruthy()
  }
})
