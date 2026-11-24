# syntax=docker/dockerfile:1
FROM node:24-alpine AS dependencies
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
RUN npm install --global pnpm@10.33.0
COPY package.json pnpm-lock.yaml ./
RUN --mount=type=cache,id=atlas-pnpm,target=/pnpm/store \
    pnpm install --frozen-lockfile --store-dir=/pnpm/store

FROM dependencies AS builder
COPY . .
# Non-secret origin only. Next.js serializes rewrites during the build.
ARG ATLAS_API_URL=http://host.docker.internal:5080
ENV ATLAS_API_URL=$ATLAS_API_URL \
    ATLAS_STANDALONE=1
RUN pnpm build

FROM node:24-alpine AS runtime
WORKDIR /app
ARG ATLAS_API_URL=http://host.docker.internal:5080
ENV NODE_ENV=production \
    ATLAS_API_URL=$ATLAS_API_URL \
    NEXT_TELEMETRY_DISABLED=1 \
    HOSTNAME=0.0.0.0 \
    PORT=3000
RUN addgroup --system --gid 1001 nodejs \
    && adduser --system --uid 1001 --ingroup nodejs nextjs
COPY --from=builder --chown=nextjs:nodejs /app/.next/standalone ./
COPY --from=builder --chown=nextjs:nodejs /app/.next/static ./.next/static
COPY --from=builder --chown=nextjs:nodejs /app/public ./public
USER nextjs
EXPOSE 3000
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD node -e "fetch('http://127.0.0.1:'+process.env.PORT+'/sign-in',{signal:AbortSignal.timeout(3000)}).then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"
CMD ["node", "server.js"]
