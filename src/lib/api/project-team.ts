import { z } from "zod"
import { api } from "./client"
import { memberSchema } from "@/data/flowboard"

export const getProjectTeamOptions = (signal?: AbortSignal) =>
  api("/members/team-options", "GET", undefined, z.array(memberSchema), signal)
