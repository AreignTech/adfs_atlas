"use client"

import * as React from "react"
import { authClient } from "@/lib/auth-client"
import { toast } from "@/components/ui/toast"

export interface EmailPasswordValues {
  email: string
  password: string
}

/** Better Auth error codes -> something a person can act on. */
const errorMessages: Record<string, string> = {
  INVALID_EMAIL_OR_PASSWORD: "That email and password combination is not correct.",
  USER_NOT_FOUND: "No account exists for that email address.",
  USER_ALREADY_EXISTS_USE_ANOTHER_EMAIL: "An account already exists for that email.",
  PASSWORD_TOO_SHORT: "Password must be at least 8 characters.",
  INVALID_EMAIL: "Enter a valid email address.",
  CREDENTIAL_ACCOUNT_NOT_FOUND: "That account uses another sign-in method.",
  SOCIAL_ACCOUNT_NOT_LINKED: "That account uses another sign-in method.",
}

function describe(error: { code?: string; message?: string } | null) {
  if (!error) return "Something went wrong. Please try again."
  if (error.code && errorMessages[error.code]) return errorMessages[error.code]
  return error.message || "Something went wrong. Please try again."
}

/**
 * Shared sign-in logic.
 *
 * Every auth form on the site calls this, so the six page variants share one
 * implementation instead of six near-identical copies.
 */
export function useEmailPasswordAuth() {
  const [pending, startTransition] = React.useTransition()

  const [error, setError] = React.useState<string | null>(null)

  const submit = React.useCallback(
    (
      values: EmailPasswordValues,
      redirectTo = "/dashboard"
    ) => {
      startTransition(async () => {
        setError(null)

        const result = await authClient.signIn.email({
          email: values.email,
          password: values.password,
        })

        if (result.error) {
          const message = describe(result.error)
          setError(message)
          toast.add({
            type: "error",
            title: "Sign-in failed",
            description: message,
          })
          return
        }

        toast.add({
          type: "success",
          title: "Welcome back",
          description: "You are signed in.",
        })

        window.location.assign(redirectTo)
      })
    },
    []
  )

  return { submit, pending, error, setError }
}
