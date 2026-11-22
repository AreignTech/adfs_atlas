"use client"

import { useEffect, useState } from "react"
import { UserPlus } from "lucide-react"
import { z } from "zod"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import {
  Field,
  FieldError,
  FieldGroup,
  FieldLabel,
} from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { toast } from "@/components/ui/toast"
import { useFlowBoard } from "@/data/flowboard-store"
import { getLabCodes, getSectionCodes } from "@/db/actions/org"
import { getRbacRoles } from "@/db/actions/rbac"

const inviteSchema = z.object({
  name: z.string().min(2, "Enter their full name."),
  email: z.string().email("Enter a valid email address."),
  role: z.string().optional(),
  roleId: z.string().uuid("Choose a role."),
  labId: z.string().uuid("Select a Lab Code."),
  sectionId: z.string().uuid("Select a Section Code."),
})

export type InviteValues = z.infer<typeof inviteSchema>

export function InviteMemberDialog() {
  const { actions } = useFlowBoard()
  const [open, setOpen] = useState(false)
  const [values, setValues] = useState<InviteValues>({
    name: "",
    email: "",
    role: "",
    roleId: "",
    labId: "",
    sectionId: "",
  })
  const [errors, setErrors] = useState<Record<string, string>>({})
  const [labCodes, setLabCodes] = useState<
    Awaited<ReturnType<typeof getLabCodes>>
  >([])
  const [sectionCodes, setSectionCodes] = useState<
    Awaited<ReturnType<typeof getSectionCodes>>
  >([])
  const [rbacRoles, setRbacRoles] = useState<
    Awaited<ReturnType<typeof getRbacRoles>>
  >([])

  useEffect(() => {
    Promise.all([
      getLabCodes(false),
      getSectionCodes(undefined, false),
      getRbacRoles(),
    ])
      .then(([labs, sections, roles]) => {
        setLabCodes(labs)
        setSectionCodes(sections)
        setRbacRoles(roles)
      })
      .catch(() => {
        setLabCodes([])
        setSectionCodes([])
        setRbacRoles([])
      })
  }, [])

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault()

    const result = inviteSchema.safeParse(values)

    if (!result.success) {
      const nextErrors: Record<string, string> = {}
      for (const issue of result.error.issues) {
        nextErrors[String(issue.path[0])] = issue.message
      }
      setErrors(nextErrors)
      return
    }

    try {
      await actions.inviteMember(result.data)

      toast.add({
        type: "success",
        title: "Invitation sent",
        description: `${result.data.name} will receive an email shortly.`,
      })

      setValues({
        name: "",
        email: "",
        role: "",
        roleId: "",
        labId: "",
        sectionId: "",
      })
      setErrors({})
      setOpen(false)
    } catch (error) {
      toast.add({
        type: "error",
        title: "Invite failed",
        description:
          error instanceof Error ? error.message : "Please try again.",
      })
    }
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger render={<Button className="cursor-pointer" />}>
        <UserPlus data-icon="inline-start" />
        Invite teammate
      </DialogTrigger>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Invite a teammate</DialogTitle>
          <DialogDescription>
            They will get access to the shared boards and can be assigned work
            straight away.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
          <FieldGroup>
            <Field data-invalid={!!errors.name}>
              <FieldLabel htmlFor="invite-name">Full name</FieldLabel>
              <Input
                id="invite-name"
                placeholder="Alex Rivera"
                value={values.name}
                onChange={(event) =>
                  setValues((prev) => ({ ...prev, name: event.target.value }))
                }
              />
              {errors.name && <FieldError errors={[{ message: errors.name }]} />}
            </Field>

            <Field data-invalid={!!errors.email}>
              <FieldLabel htmlFor="invite-email">Work email</FieldLabel>
              <Input
                id="invite-email"
                type="email"
                placeholder="alex@adfs.local"
                value={values.email}
                onChange={(event) =>
                  setValues((prev) => ({ ...prev, email: event.target.value }))
                }
              />
              {errors.email && <FieldError errors={[{ message: errors.email }]} />}
            </Field>

            <Field>
              <FieldLabel htmlFor="invite-role">Role</FieldLabel>
              <Select
                items={rbacRoles.map((role) => ({
                  value: role.id,
                  label: role.name,
                }))}
                value={values.roleId}
                onValueChange={(value) => {
                  if (!value) return
                  const role = rbacRoles.find((entry) => entry.id === value)
                  setValues((prev) => ({
                    ...prev,
                    roleId: value,
                    role: role?.name ?? "",
                  }))
                }}
              >
                <SelectTrigger id="invite-role" className="w-full cursor-pointer">
                  <SelectValue placeholder="Select role" />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    {rbacRoles.map((role) => (
                      <SelectItem key={role.id} value={role.id}>
                        {role.name}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>

            <Field data-invalid={!!errors.labId}>
              <FieldLabel htmlFor="invite-lab">Lab Code</FieldLabel>
              <Select
                items={labCodes.map((lab) => ({
                  value: lab.id,
                  label: `${lab.code} - ${lab.name}`,
                }))}
                value={values.labId}
                onValueChange={(value) =>
                  value && setValues((prev) => ({ ...prev, labId: value }))
                }
              >
                <SelectTrigger id="invite-lab" className="w-full cursor-pointer">
                  <SelectValue placeholder="Select Lab" />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    {labCodes.map((lab) => (
                      <SelectItem key={lab.id} value={lab.id}>
                        {lab.code} - {lab.name}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
              {errors.labId && <FieldError errors={[{ message: errors.labId }]} />}
            </Field>

            <Field data-invalid={!!errors.sectionId}>
              <FieldLabel htmlFor="invite-section">Section Code</FieldLabel>
              <Select
                items={sectionCodes.map((section) => ({
                  value: section.id,
                  label: `${section.code} - ${section.name}`,
                }))}
                value={values.sectionId}
                onValueChange={(value) =>
                  value && setValues((prev) => ({ ...prev, sectionId: value }))
                }
              >
                <SelectTrigger id="invite-section" className="w-full cursor-pointer">
                  <SelectValue placeholder="Select Section" />
                </SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    {sectionCodes.map((section) => (
                      <SelectItem key={section.id} value={section.id}>
                        {section.code} - {section.name}
                      </SelectItem>
                    ))}
                  </SelectGroup>
                </SelectContent>
              </Select>
              {errors.sectionId && <FieldError errors={[{ message: errors.sectionId }]} />}
            </Field>
          </FieldGroup>

          <DialogFooter>
            <Button type="submit" className="cursor-pointer">
              Send invite
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}