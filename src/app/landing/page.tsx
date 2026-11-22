import type { Metadata } from "next"
import { LandingPageContent } from "./landing-page-content"

// Metadata for the landing page
export const metadata: Metadata = {
  title: "ATLAS - ADFS Task Lifecycle and Assignment System",
  description:
    "A centralized task lifecycle and assignment management platform for ADFS.",
  keywords: [
    "project management",
    "task tracking",
    "team collaboration",
    "progress analytics",
    "deadline tracking",
    "kanban",
  ],
  openGraph: {
    title: "ATLAS - ADFS Task Lifecycle and Assignment System",
    description:
      "Organize projects, manage tasks, track progress, and never miss a deadline.",
    type: "website",
  },
  twitter: {
    card: "summary_large_image",
    title: "ATLAS - ADFS Task Lifecycle and Assignment System",
    description:
      "Organize projects, manage tasks, track progress, and never miss a deadline.",
  },
}

export default function LandingPage() {
  return <LandingPageContent />
}