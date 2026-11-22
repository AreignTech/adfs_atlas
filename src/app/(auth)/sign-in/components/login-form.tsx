"use client"

import { zodResolver } from "@hookform/resolvers/zod"
import { Controller, useForm } from "react-hook-form"
import { z } from "zod"
import { cn } from "@/lib/utils"
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
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { useEmailPasswordAuth } from "@/hooks/use-email-password"
import * as React from "react"
const loginFormSchema = z.object({
  email: z.string(),
  password: z.string(),
})

type LoginFormValues = z.infer<typeof loginFormSchema>

export function LoginForm({
  className,
  redirectTo = "/dashboard",
  ...props
}: React.ComponentProps<"div"> & { redirectTo?: string }) {
  const { submit, pending, error } = useEmailPasswordAuth()
  const [ldapEnabled, setLdapEnabled] = React.useState(false)
  const [authMode, setAuthMode] = React.useState<"atlas" | "ldap">("atlas")
  const [ldapPending, setLdapPending] = React.useState(false)
  const [ldapError, setLdapError] = React.useState<string | null>(null)

  React.useEffect(() => {
    fetch("/api/auth/ldap/status")
      .then((response) => response.json())
      .then((status: { enabled?: boolean }) => {
        setLdapEnabled(Boolean(status.enabled))
      })
      .catch(() => setLdapEnabled(false))
  }, [])

  const form = useForm<LoginFormValues>({
    resolver: zodResolver(loginFormSchema),
    defaultValues: {
      email: "",
      password: "",
    },
  })

  function onSubmit(values: LoginFormValues) {
    if (authMode === "atlas") {
      if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(values.email)) {
        form.setError("email", {
          type: "manual",
          message: "Invalid email address",
        })
        return
      }
      submit(values, redirectTo)
      return
    }
    if (!values.email.trim() || !values.password) {
      setLdapError("Enter your directory username and password.")
      return
    }
    setLdapPending(true)
    setLdapError(null)
    fetch("/api/auth/ldap", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        username: values.email,
        password: values.password,
      }),
    })
      .then(async (response) => {
        const data = await response.json()
        if (!response.ok) {
          throw new Error(data.error || "Directory authentication failed.")
        }
        return data
      })
      .then(() => {
        window.location.assign(redirectTo)
      })
      .catch((ldapFailure: unknown) => {
        setLdapError(
          ldapFailure instanceof Error
            ? ldapFailure.message
            : "Directory authentication failed."
        )
      })
      .finally(() => setLdapPending(false))
  }

  return (
    <div className={cn("flex flex-col gap-6", className)} {...props}>
      <Card>
        <CardHeader className="text-center">
          <CardTitle className="text-xl">Welcome back</CardTitle>
          <CardDescription>
            Enter your email below to login to your account
          </CardDescription>
        </CardHeader>
        <CardContent>
          {ldapEnabled && (
            <div className="mb-5 grid grid-cols-2 gap-2 rounded-lg border p-1">
              <Button
                type="button"
                variant={authMode === "atlas" ? "secondary" : "ghost"}
                className="cursor-pointer"
                onClick={() => {
                  setAuthMode("atlas")
                  setLdapError(null)
                }}
              >
                ATLAS
              </Button>
              <Button
                type="button"
                variant={authMode === "ldap" ? "secondary" : "ghost"}
                className="cursor-pointer"
                onClick={() => {
                  setAuthMode("ldap")
                  setLdapError(null)
                }}
              >
                LDAP
              </Button>
            </div>
          )}
          <form onSubmit={form.handleSubmit(onSubmit)}>
            <div className="grid gap-6">
              <FieldGroup className="grid gap-4">
                <Controller
                  control={form.control}
                  name="email"
                  render={({ field, fieldState }) => (
                    <Field data-invalid={fieldState.invalid}>
                      <FieldLabel htmlFor="login-email">
                        {authMode === "ldap" ? "Directory username" : "Email"}
                      </FieldLabel>

                      <Input
                        id="login-email"
                        aria-invalid={fieldState.invalid}
                        type={authMode === "ldap" ? "text" : "email"}
                        placeholder={
                          authMode === "ldap"
                            ? "Directory username"
                            : "you@adfs.local"
                        }
                        {...field}
                      />
                      {fieldState.invalid && (
                        <FieldError errors={[fieldState.error]} />
                      )}
                    </Field>
                  )}
                />
                <Controller
                  control={form.control}
                  name="password"
                  render={({ field, fieldState }) => (
                    <Field data-invalid={fieldState.invalid}>
                      <FieldLabel htmlFor="login-password">
                        Password
                      </FieldLabel>

                      <Input
                        id="login-password"
                        aria-invalid={fieldState.invalid}
                        type="password"
                        {...field}
                      />
                      {fieldState.invalid && (
                        <FieldError errors={[fieldState.error]} />
                      )}
                    </Field>
                  )}
                />
                {error && (
                  <p
                    role="alert"
                    className="text-sm text-destructive text-center"
                  >
                    {error}
                  </p>
                )}
                {ldapError && (
                  <p
                    role="alert"
                    className="text-sm text-destructive text-center"
                  >
                    {ldapError}
                  </p>
                )}

                <Button
                  type="submit"
                  className="w-full cursor-pointer"
                  disabled={pending || ldapPending}
                >
                  {pending || ldapPending ? "Signing in..." : "Login"}
                </Button>
              </FieldGroup>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
