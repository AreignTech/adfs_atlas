"use client"

import { zodResolver } from "@hookform/resolvers/zod"
import { Controller, useForm, useWatch } from "react-hook-form"
import { z } from "zod"
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/components/ui/field"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardHeader,
  CardDescription,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { Upload } from "lucide-react"
import { useEffect, useRef, useState } from "react"
import { useRouter } from "next/navigation"
import { api } from "@/lib/api/client"
import { Separator } from "@/components/ui/separator"
import { toast } from "@/components/ui/toast"
import { Logo } from "@/components/logo"

const userFormSchema = z.object({
  firstName: z.string().min(1, "First name is required"),
  lastName: z.string().min(1, "Last name is required"),
  email: z.string().email("Invalid email address"),
  phone: z.string().optional(),
  website: z.string().optional(),
  location: z.string().optional(),
  role: z.string().optional(),
  bio: z.string().optional(),
  company: z.string().optional(),
  timezone: z.string().optional(),
  language: z.string().optional(),
})

type UserFormValues = z.infer<typeof userFormSchema>

export default function UserSettingsPage() {
  const router = useRouter()
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [savedImage, setSavedImage] = useState<string | null>(null)
  const [profileImage, setProfileImage] = useState<string | null>(null)
  const [useDefaultIcon, setUseDefaultIcon] = useState(true)
  const [imageError, setImageError] = useState("")
  const [readingImage, setReadingImage] = useState(false)
  const [profileReady, setProfileReady] = useState(false)
  const [profileLoadError, setProfileLoadError] = useState("")
  const [loadAttempt, setLoadAttempt] = useState(0)

  const form = useForm<UserFormValues>({
    resolver: zodResolver(userFormSchema),
    defaultValues: {
      firstName: "",
      lastName: "",
      email: "",
      phone: "",
      website: "",
      location: "",
      role: "",
      bio: "",
      company: "",
      timezone: "",
      language: "English",
    },
  })
  const [firstName, lastName] = useWatch({ control: form.control, name: ["firstName", "lastName"] })

  useEffect(() => {
    let active = true
    api<{ user: { name: string; email: string; image: string | null }; profile: (UserFormValues & { profileImage?: string | null }) | null }>("/settings/profile")
      .then((data) => {
        if (!active) return
        const parts = data.user.name.split(" ")
        form.reset(data.profile ?? { firstName: parts[0], lastName: parts.slice(1).join(" "), email: data.user.email })
        setProfileImage(data.user.image)
        setSavedImage(data.user.image)
        setUseDefaultIcon(!data.user.image)
        setProfileReady(true)
      }).catch((error: unknown) => { if (active) setProfileLoadError(error instanceof Error ? error.message : "Could not load your profile. Please try again.") })
    return () => { active = false }
  }, [form, loadAttempt])

  async function onSubmit(data: UserFormValues) {
    if (!profileReady || readingImage) return
    try { await api("/settings/profile", "POST", { ...data, profileImage }) }
    catch (error) { toast.add({ type: "error", title: "Could not save profile", description: error instanceof Error ? error.message : "Please try again." }); return }
    toast.add({
      type: "success",
      title: "Profile updated",
      description: `${data.firstName} ${data.lastName}'s profile was saved.`,
    })
    setSavedImage(profileImage)
    form.reset(data)
    router.refresh()
  }

  const handleFileUpload = () => {
    fileInputRef.current?.click()
  }

  const handleFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    event.target.value = ""
    if (file) {
      setImageError("")
      if (!["image/jpeg", "image/png", "image/gif", "image/webp"].includes(file.type)) {
        setImageError("Choose a JPG, PNG, GIF or WebP image.")
        return
      }
      if (file.size > 2 * 1024 * 1024) {
        setImageError("Your photo is too large. Choose an image no larger than 2 MB.")
        return
      }
      setReadingImage(true)
      const reader = new FileReader()
      reader.onload = (e) => {
        if (typeof e.target?.result === "string") {
          setProfileImage(e.target.result)
          setUseDefaultIcon(false)
        }
        setReadingImage(false)
      }
      reader.onerror = () => { setImageError("Could not read this image. Please choose it again."); setReadingImage(false) }
      reader.readAsDataURL(file)
    }
  }

  const handleReset = () => {
    setImageError("")
    setProfileImage(null)
    setUseDefaultIcon(true)
    if (fileInputRef.current) {
      fileInputRef.current.value = ""
    }
  }

  return (
    <div className="flex flex-col gap-6 px-4 lg:px-6">
      <div>
        <h1 className="text-3xl font-bold">Profile</h1>
        <p className="text-muted-foreground">
          Your personal details, timezone and the defaults you work with.
        </p>
      </div>
      <form onSubmit={form.handleSubmit(onSubmit)}>
        <Card>
          <CardHeader>
            <CardTitle>Profile</CardTitle>
            <CardDescription>
              Update your personal information and how you appear to the rest
              of the team.
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-6">
            {profileLoadError && <div role="alert" className="space-y-2 text-sm text-destructive">
              <p>{profileLoadError}</p>
              <Button type="button" variant="outline" size="sm" onClick={() => { setProfileLoadError(""); setLoadAttempt((current) => current + 1) }}>Retry loading profile</Button>
            </div>}
            {/* Profile Picture Section */}
            <div className="flex items-center gap-6 ">
              {useDefaultIcon ? (
                <div className="flex size-20 items-center justify-center rounded-lg">
                  <Logo size={56} />
                </div>
              ) : (
                <Avatar className="size-20 rounded-lg">
                  <AvatarImage src={profileImage || undefined} alt="Profile photo" />
                  <AvatarFallback>{[firstName, lastName].map((part) => part[0] ?? "").join("").toUpperCase()}</AvatarFallback>
                </Avatar>
              )}
              <div className="flex flex-col gap-2">
                <div className="flex gap-2">
                  <Button
                    type="button"
                    disabled={!profileReady || readingImage || form.formState.isSubmitting}
                    variant="default"
                    size="sm"
                    onClick={handleFileUpload}
                    className="cursor-pointer"
                  >
                    <Upload data-icon="inline-start" />
                    Upload new photo
                  </Button>
                  <Button
                    type="button"
                    disabled={!profileReady || readingImage || form.formState.isSubmitting}
                    variant="outline"
                    size="sm"
                    onClick={handleReset}
                    className="cursor-pointer"
                  >
                    Reset
                  </Button>
                </div>
                <p className="text-xs text-muted-foreground">
                  JPG, PNG, GIF or WebP. Maximum size: 2 MB.
                </p>
                {readingImage && <p role="status" className="text-xs text-muted-foreground">Reading photo…</p>}
                {imageError && <p role="alert" className="text-sm text-destructive">{imageError}</p>}
              </div>
              <input
                ref={fileInputRef}
                type="file"
                aria-label="Profile photo upload"
                accept="image/jpeg,image/gif,image/png,image/webp"
                onChange={handleFileChange}
                className="hidden"
              />
            </div>

            <Separator className="mb-4" />
            {/* Form Fields */}
            <FieldGroup className="grid grid-cols-1 md:grid-cols-2 gap-6">
              {/* First Name */}
              <Controller
                control={form.control}
                name="firstName"
                render={({ field, fieldState }) => (
                  <Field data-invalid={fieldState.invalid}>
                    <FieldLabel htmlFor="user-first-name">
                      First Name
                    </FieldLabel>

                    <Input
                      id="user-first-name"
                      aria-invalid={fieldState.invalid}
                      placeholder="Enter your first name"
                      {...field}
                    />
                    {fieldState.invalid && (
                      <FieldError errors={[fieldState.error]} />
                    )}
                  </Field>
                )}
              />

              {/* Last Name */}
              <Controller
                control={form.control}
                name="lastName"
                render={({ field, fieldState }) => (
                  <Field data-invalid={fieldState.invalid}>
                    <FieldLabel htmlFor="user-last-name">Last Name</FieldLabel>

                    <Input
                      id="user-last-name"
                      aria-invalid={fieldState.invalid}
                      placeholder="Enter your last name"
                      {...field}
                    />
                    {fieldState.invalid && (
                      <FieldError errors={[fieldState.error]} />
                    )}
                  </Field>
                )}
              />

              {/* Email */}
              <Controller
                control={form.control}
                name="email"
                render={({ field, fieldState }) => (
                  <Field data-invalid={fieldState.invalid}>
                    <FieldLabel htmlFor="user-email">E-mail</FieldLabel>

                    <Input
                      id="user-email"
                      aria-invalid={fieldState.invalid}
                      type="email"
                      placeholder="Enter your email"
                      {...field}
                    />
                    {fieldState.invalid && (
                      <FieldError errors={[fieldState.error]} />
                    )}
                  </Field>
                )}
              />

              {/* Company */}
              <Controller
                control={form.control}
                name="company"
                render={({ field, fieldState }) => (
                  <Field data-invalid={fieldState.invalid}>
                    <FieldLabel htmlFor="user-company">Company</FieldLabel>

                    <Input
                      id="user-company"
                      aria-invalid={fieldState.invalid}
                      placeholder="Enter your company"
                      {...field}
                    />
                    {fieldState.invalid && (
                      <FieldError errors={[fieldState.error]} />
                    )}
                  </Field>
                )}
              />

              {/* Phone Number */}
              <Controller
                control={form.control}
                name="phone"
                render={({ field, fieldState }) => (
                  <Field data-invalid={fieldState.invalid}>
                    <FieldLabel htmlFor="user-phone">Phone Number</FieldLabel>

                    <Input
                      id="user-phone"
                      aria-invalid={fieldState.invalid}
                      type="tel"
                      placeholder="Enter your phone number"
                      {...field}
                    />
                    {fieldState.invalid && (
                      <FieldError errors={[fieldState.error]} />
                    )}
                  </Field>
                )}
              />

              {/* Location */}
              <Controller
                control={form.control}
                name="location"
                render={({ field, fieldState }) => (
                  <Field data-invalid={fieldState.invalid}>
                    <FieldLabel htmlFor="user-location">Location</FieldLabel>

                    <Input
                      id="user-location"
                      aria-invalid={fieldState.invalid}
                      placeholder="Enter your location"
                      {...field}
                    />
                    {fieldState.invalid && (
                      <FieldError errors={[fieldState.error]} />
                    )}
                  </Field>
                )}
              />

              {/* Website */}
              <Controller
                control={form.control}
                name="website"
                render={({ field, fieldState }) => (
                  <Field data-invalid={fieldState.invalid}>
                    <FieldLabel htmlFor="user-website">Website</FieldLabel>

                    <Input
                      id="user-website"
                      aria-invalid={fieldState.invalid}
                      type="url"
                      placeholder="Enter your website"
                      {...field}
                    />
                    {fieldState.invalid && (
                      <FieldError errors={[fieldState.error]} />
                    )}
                  </Field>
                )}
              />

              {/* Language */}
              <Controller
                control={form.control}
                name="language"
                render={({ field, fieldState }) => (
                  <Field data-invalid={fieldState.invalid}>
                    <FieldLabel htmlFor="user-language">Language</FieldLabel>
                    <Select
                      items={{
                        english: "English",
                        spanish: "Spanish",
                        french: "French",
                        german: "German",
                        italian: "Italian",
                        portuguese: "Portuguese",
                      }}
                      name={field.name}
                      onValueChange={field.onChange}
                      value={field.value}
                    >
                      <SelectTrigger
                        id="user-language"
                        aria-invalid={fieldState.invalid}
                        className="w-full"
                      >
                        <SelectValue placeholder="Select Language" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectGroup>
                          <SelectItem value="english">English</SelectItem>
                          <SelectItem value="spanish">Spanish</SelectItem>
                          <SelectItem value="french">French</SelectItem>
                          <SelectItem value="german">German</SelectItem>
                          <SelectItem value="italian">Italian</SelectItem>
                          <SelectItem value="portuguese">Portuguese</SelectItem>
                        </SelectGroup>
                      </SelectContent>
                    </Select>
                    {fieldState.invalid && (
                      <FieldError errors={[fieldState.error]} />
                    )}
                  </Field>
                )}
              />

              {/* Role */}
              <Controller
                control={form.control}
                name="role"
                render={({ field, fieldState }) => (
                  <Field data-invalid={fieldState.invalid}>
                    <FieldLabel htmlFor="user-role">Role</FieldLabel>

                    <Input
                      id="user-role"
                      aria-invalid={fieldState.invalid}
                      placeholder="Enter your role"
                      {...field}
                    />
                    {fieldState.invalid && (
                      <FieldError errors={[fieldState.error]} />
                    )}
                  </Field>
                )}
              />

              {/* Timezone */}
              <Controller
                control={form.control}
                name="timezone"
                render={({ field, fieldState }) => (
                  <Field data-invalid={fieldState.invalid}>
                    <FieldLabel htmlFor="user-timezone">Timezone</FieldLabel>
                    <Select
                      items={{
                        pst: "PST (Pacific Standard Time)",
                        est: "EST (Eastern Standard Time)",
                        cst: "CST (Central Standard Time)",
                        mst: "MST (Mountain Standard Time)",
                        utc: "UTC (Coordinated Universal Time)",
                        cet: "CET (Central European Time)",
                        jst: "JST (Japan Standard Time)",
                        aest: "AEST (Australian Eastern Standard Time)",
                      }}
                      name={field.name}
                      onValueChange={field.onChange}
                      value={field.value}
                    >
                      <SelectTrigger
                        id="user-timezone"
                        aria-invalid={fieldState.invalid}
                        className="w-full"
                      >
                        <SelectValue placeholder="Select Timezone" />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectGroup>
                          <SelectItem value="pst">
                            PST (Pacific Standard Time)
                          </SelectItem>
                          <SelectItem value="est">
                            EST (Eastern Standard Time)
                          </SelectItem>
                          <SelectItem value="cst">
                            CST (Central Standard Time)
                          </SelectItem>
                          <SelectItem value="mst">
                            MST (Mountain Standard Time)
                          </SelectItem>
                          <SelectItem value="utc">
                            UTC (Coordinated Universal Time)
                          </SelectItem>
                          <SelectItem value="cet">
                            CET (Central European Time)
                          </SelectItem>
                          <SelectItem value="jst">
                            JST (Japan Standard Time)
                          </SelectItem>
                          <SelectItem value="aest">
                            AEST (Australian Eastern Standard Time)
                          </SelectItem>
                        </SelectGroup>
                      </SelectContent>
                    </Select>
                    {fieldState.invalid && (
                      <FieldError errors={[fieldState.error]} />
                    )}
                  </Field>
                )}
              />
            </FieldGroup>

            {/* Bio - Full Width */}
            <Controller
              control={form.control}
              name="bio"
              render={({ field, fieldState }) => (
                <Field data-invalid={fieldState.invalid}>
                  <FieldLabel htmlFor="user-bio">Bio</FieldLabel>

                  <Textarea
                    id="user-bio"
                    aria-invalid={fieldState.invalid}
                    placeholder="Tell us a little about yourself..."
                    className="min-h-[100px]"
                    {...field}
                  />
                  {fieldState.invalid && (
                    <FieldError errors={[fieldState.error]} />
                  )}
                </Field>
              )}
            />

            {/* Action Buttons */}
            <div className="flex justify-start gap-3">
              <Button type="submit" className="cursor-pointer" disabled={!profileReady || readingImage || form.formState.isSubmitting}>
                {form.formState.isSubmitting ? "Saving…" : "Save changes"}
              </Button>
              <Button
                variant="outline"
                type="button"
                className="cursor-pointer"
                disabled={readingImage || form.formState.isSubmitting}
                onClick={() => { form.reset(); setProfileImage(savedImage); setUseDefaultIcon(!savedImage); setImageError("") }}
              >
                Cancel
              </Button>
            </div>
          </CardContent>
        </Card>
      </form>
    </div>
  )
}
