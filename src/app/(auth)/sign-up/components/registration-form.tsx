"use client"

import { useEffect, useState } from "react"
import Link from "next/link"
import { useRouter } from "next/navigation"
import { useForm } from "react-hook-form"
import { zodResolver } from "@hookform/resolvers/zod"
import { z } from "zod"
import { api, ApiError } from "@/lib/api/client"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Field, FieldError, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"

const schema = z.object({
  firstName: z.string().trim().min(1, "Enter your first name.").max(100),
  lastName: z.string().trim().min(1, "Enter your last name.").max(100),
  email: z.email("Enter a valid email address.").max(255),
  password: z.string().min(8, "Use at least 8 characters.").max(4000)
    .regex(/[A-Z]/, "Include an uppercase letter.")
    .regex(/[0-9]/, "Include a number."),
  confirmPassword: z.string().min(1, "Confirm your password."),
}).refine((values) => values.password === values.confirmPassword, {
  message: "Passwords do not match.", path: ["confirmPassword"],
})
type Values = z.infer<typeof schema>

const fields = [
  { name: "firstName", label: "First Name", placeholder: "First name", autoComplete: "given-name" },
  { name: "lastName", label: "Last Name", placeholder: "Last name", autoComplete: "family-name" },
  { name: "email", label: "Email Address", placeholder: "you@example.com", autoComplete: "email" },
  { name: "password", label: "Password", placeholder: "Create a password", autoComplete: "new-password" },
  { name: "confirmPassword", label: "Confirm Password", placeholder: "Repeat your password", autoComplete: "new-password" },
] as const

export function RegistrationForm() {
  const [showPassword, setShowPassword] = useState(false)
  const [success, setSuccess] = useState(false)
  const router = useRouter()
  const { register, handleSubmit, setError, formState: { errors, isSubmitting } } = useForm<Values>({ resolver: zodResolver(schema) })
  useEffect(() => {
    if (!success) return
    const timer = window.setTimeout(() => router.push("/dashboard"), 2000)
    return () => window.clearTimeout(timer)
  }, [success, router])

  async function onSubmit(values: Values) {
    if (isSubmitting) return
    try {
      await api("/auth/register", "POST", {
        name: `${values.firstName} ${values.lastName}`,
        email: values.email,
        password: values.password,
      })
      setSuccess(true)
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        setError("email", { message: "An account already exists for that email address." }, { shouldFocus: true })
      } else {
        setError("root", { message: error instanceof ApiError && error.status < 500
          ? error.message : "Registration failed. Please try again later." })
      }
    }
  }

  return (
    <Card>
      <CardHeader className="text-center">
        <CardTitle className="text-xl">Create Your Account</CardTitle>
        <CardDescription>Welcome to ATLAS. Register to access your workspace.</CardDescription>
      </CardHeader>
      <CardContent>
        {success ? (
          <div className="grid gap-4 text-center">
            <p role="status" className="text-sm">Registration successful. You are signed in.</p>
            <Button nativeButton={false} render={<Link href="/dashboard" />}>Continue to Dashboard</Button>
          </div>
        ) : (
          <form onSubmit={handleSubmit(onSubmit)} noValidate className="grid gap-4">
            <p className="text-muted-foreground text-xs">All fields are required.</p>
            {fields.map(({ name, label, placeholder, autoComplete }) => (
              <Field key={name} data-invalid={Boolean(errors[name])}>
                <FieldLabel htmlFor={`register-${name}`}>{label} *</FieldLabel>
                <Input id={`register-${name}`} placeholder={placeholder} autoComplete={autoComplete}
                  type={name === "email" ? "email" : name.toLowerCase().includes("password") && !showPassword ? "password" : "text"}
                  required aria-invalid={Boolean(errors[name])}
                  aria-describedby={errors[name] ? `${name}-error` : name === "password" ? "password-help" : undefined}
                  {...register(name)} />
                {name === "password" && <p id="password-help" className="text-muted-foreground text-xs">Use at least 8 characters with an uppercase letter and a number.</p>}
                {errors[name] && <FieldError id={`${name}-error`} errors={[errors[name]]} />}
              </Field>
            ))}
            <label className="flex cursor-pointer items-center gap-2 text-sm">
              <input type="checkbox" checked={showPassword} onChange={(event) => setShowPassword(event.target.checked)} />
              Show passwords
            </label>
            {errors.root && <p role="alert" className="text-destructive text-sm">{errors.root.message}</p>}
            <Button type="submit" disabled={isSubmitting} className="w-full cursor-pointer">
              {isSubmitting ? "Creating account..." : "Create Account"}
            </Button>
          </form>
        )}
        <p className="mt-6 text-center text-sm text-muted-foreground">
          Already have an account? <Link href="/sign-in" className="text-primary underline underline-offset-4">Log In</Link>
        </p>
      </CardContent>
    </Card>
  )
}
