# Use an official Node.js runtime as a parent image
FROM node:lts-alpine AS build

# Create app directory
WORKDIR /usr/src/app

# Install app dependencies
COPY package*.json ./
RUN npm install

# Bundle app source
COPY . .

RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

# Use the same official Node.js runtime as a parent image for the final stage
FROM node:lts-alpine

# Set the working directory in the container for the final stage
WORKDIR /usr/src/app

# Copy the application code and dependencies from the build stage
COPY --from=build /usr/src/app .

# Expose ports (if needed)
EXPOSE 3000

# Start the server
CMD ["node", "server.js"]
