import * as React from "react"

interface LogoProps extends React.SVGProps<SVGSVGElement> {
  size?: number
}

export function Logo({ size = 24, className, ...props }: LogoProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 32 32"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      {...props}
    >
      <path
        d="M16 2.75 27.25 9.15v13.7L16 29.25 4.75 22.85v-13.7L16 2.75Z"
        stroke="currentColor"
        strokeWidth="1.75"
        strokeLinejoin="round"
      />
      <path
        d="m8.75 22.25 7.25-15 7.25 15M11.25 17h9.5"
        stroke="currentColor"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle cx="16" cy="7.25" r="1.5" fill="currentColor" />
      <circle cx="8.75" cy="22.25" r="1.5" fill="currentColor" />
      <circle cx="23.25" cy="22.25" r="1.5" fill="currentColor" />
    </svg>
  )
}