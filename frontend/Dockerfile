# Stage 1: Build the React application
FROM node:22-alpine AS build

WORKDIR /app

# Copy dependency manifests first
COPY package.json package-lock.json ./

# Install dependencies without running package lifecycle scripts
RUN npm ci --ignore-scripts

# Copy the frontend source code
COPY . .

# Build the static website
RUN npm run build


# Stage 2: Serve only the built files
FROM nginxinc/nginx-unprivileged:alpine AS runtime

COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 8080
