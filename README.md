## Running the project with Docker

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/) with Docker Compose v2 (`docker compose version` should work)
- Free ports: `5173` (frontend), `8080` (API), `5432` (PostgreSQL)

### Start everything

From the repository root:

```bash
docker compose up --build
```

The first build takes a few minutes. Compose starts the services in this order:

1. **postgres**: the database, started first and checked for health.
2. **migrate**: applies all EF Core migrations, then exits. This is expected.
3. **server** and **frontend**: start once the migrations have finished successfully.

When the logs settle, open:

| What | URL |
|------|-----|
| Frontend | http://localhost:5173 |
| API | http://localhost:8080 |

Add `-d` to run in the background (`docker compose up --build -d`).

### Everyday commands

```bash
docker compose up -d                 # start in the background
docker compose logs -f server        # follow the logs of one service
docker compose ps                    # show what is running
docker compose down                  # stop and remove the containers (data is kept)
docker compose down -v               # also delete the database volume (full reset)
```

### After pulling new changes

Rebuild the images so code changes take effect:

```bash
docker compose up --build
```

If the new code contains database migrations, they are applied automatically by the `migrate` service.

### Starting a single service

```bash
docker compose up -d postgres            # only the database
docker compose up --build server         # server plus its dependencies (postgres, migrate)
docker compose up --no-deps server       # only the server, without dependencies
docker compose run --rm migrate          # run the migrations once
```

The frontend and server need the database, so start `postgres` first if you use `--no-deps`.

### Configuration

The database connection string can be overridden with the `CODEMAZE_CONNECTION` environment variable. The default points at the bundled Postgres container:

```bash
CODEMAZE_CONNECTION="Host=postgres;Port=5432;Database=codemaze;Username=codemaze;Password=password" \
  docker compose up
```

The default credentials are for local development only. Do not reuse them anywhere else.

### Troubleshooting

- **Port already in use:** stop whatever uses the port, or change the left side of the mapping in `docker-compose.yml` (for example `"5433:5432"`).
- **Migrations fail or the database is in a bad state:** run `docker compose down -v` and start again. This deletes all local data.
- **Changes don't show up:** you probably started without `--build`. Run `docker compose up --build`.
- **Build errors after changing packages:** rebuild without cache with `docker compose build --no-cache`.