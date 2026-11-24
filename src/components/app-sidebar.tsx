"use client"

import * as React from "react"
import {
  CalendarClock,
  FolderKanban,
  LayoutDashboard,
  ListTodo,
  Settings,
  ShieldCheck,
  Building2,
  Users,
} from "lucide-react"
import Link from "next/link"
import { Logo } from "@/components/logo"
import { SidebarNotification } from "@/components/sidebar-notification"

import { NavMain } from "@/components/nav-main"
import { NavUser } from "@/components/nav-user"
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar"

export interface SidebarUser {
  name: string
  email: string
  image: string | null
}

const navGroups = [
    {
      label: "Workspace",
      items: [
        {
          title: "Overview",
          url: "/dashboard",
          icon: LayoutDashboard,
        },
        {
          title: "Projects",
          url: "/projects",
          icon: FolderKanban,
        },
        {
          title: "Tasks",
          url: "/tasks",
          icon: ListTodo,
        },
        {
          title: "Calendar",
          url: "/calendar",
          icon: CalendarClock,
        },
      ],
    },
    {
      label: "Account",
      items: [
        {
          title: "Settings",
          url: "#",
          icon: Settings,
          items: [
            {
              title: "Profile",
              url: "/settings/user",
            },
            {
              title: "Security",
              url: "/settings/account",
            },
            {
              title: "Notifications",
              url: "/settings/notifications",
            },
          ],
        },
      ],
    },
]

export function AppSidebar({
  user,
  isAdministrator = false,
  ...props
}: {
  user: SidebarUser
  isAdministrator?: boolean
} & React.ComponentProps<typeof Sidebar>) {
  const visibleNavGroups = [
    ...navGroups,
    ...(isAdministrator
      ? [
          {
            label: "Administration",
            items: [
              {
                title: "Users",
                url: "/users",
                icon: Users,
              },
              {
                title: "Roles",
                url: "/settings/roles",
                icon: ShieldCheck,
              },
              {
                title: "Auth Settings",
                url: "/settings/authentication",
                icon: Settings,
              },
              {
                title: "Org Structure",
                url: "#",
                icon: Building2,
                items: [
                  {
                    title: "Lab",
                    url: "/settings/labs",
                  },
                  {
                    title: "Section",
                    url: "/settings/sections",
                  },
                ],
              },
              {
                title: "Audit Logs",
                url: "/settings/audit",
                icon: ListTodo,
              },
              {
                title: "API Management",
                url: "/settings/api-management",
                icon: Settings,
                items: [
                  { title: "API Health and Statistics", url: "/settings/api-management" },
                  { title: "API Documentation", url: "/settings/api-management/documentation" },
                  { title: "API Activity Logs", url: "/settings/api-management/activity" },
                  { title: "API Diagnostics", url: "/settings/api-management/diagnostics" },
                  { title: "API Configuration", url: "/settings/api-management/configuration" },
                ],
              },
            ],
          },
        ]
      : []),
  ]
  return (
    <Sidebar {...props}>
      <SidebarHeader>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton size="lg" render={<Link href="/dashboard" />}>
              <div className="flex aspect-square size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground">
                <Logo size={24} className="text-current" />
              </div>
              <div className="grid flex-1 text-left text-sm leading-tight">
                <span className="truncate font-medium">ATLAS</span>
                <span className="truncate text-xs">ADFS Task Lifecycle</span>
              </div>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarHeader>
      <SidebarContent>
        {visibleNavGroups.map((group) => (
          <NavMain key={group.label} label={group.label} items={group.items} />
        ))}
      </SidebarContent>
      <SidebarFooter>
        <SidebarNotification isAdministrator={isAdministrator} />
        <NavUser user={user} />
      </SidebarFooter>
    </Sidebar>
  )
}
