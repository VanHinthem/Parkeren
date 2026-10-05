# Published image examples

These examples pull public images from GHCR. They do not replace the active files under `deploy/`. Copy the example files to a stable folder on the NAS; the application does not need a Git checkout to run. Do not use them to change a running NAS stack until its owner confirms the current Compose project name and database volume.

## Before you start

You need Docker Compose v2 on the NAS and these example files copied there. To generate passwords or Web Push keys, use a trusted computer with Node.js installed (npm/npx is included). The commands below assume the examples are copied with the same `deploy/examples` folder path and are run from the directory above `deploy/`; adjust paths if you put them elsewhere. The production example needs a published production image; set the explicit release or immutable SHA tag before pulling or starting it.

Never commit a populated `.env` file or share its contents. It contains database and provider credentials, and may contain a Web Push private key. The plain `docker compose config` command prints resolved environment values; use `config --quiet` for validation and do not paste full config output into tickets or chat.

## Prepare the env files

Copy the templates next to the Compose examples, then edit the copies, not the `.example` files:

```sh
cp deploy/examples/.env.production.example deploy/examples/.env.production
cp deploy/examples/.env.development.example deploy/examples/.env.development
chmod 600 deploy/examples/.env.production deploy/examples/.env.development
```

Generate a different strong database password for each environment with Node.js:

```sh
node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"
```

Copy the output into the matching `PARKEREN_DB_PASSWORD` value. Treat it as a secret. Replace every `REPLACE_WITH_...` and `replace-with-...` value in the production file; use real TwoPark credentials there. The development stack uses the mock and must have its own database password.

## Optional browser push

Web Push sends notifications to browsers. It is optional: leave all three `PARKEREN_WEBPUSH_...` values empty and the app will continue to work, but browser push notifications will be skipped.

To enable it, install Node.js (which includes npm) on a trusted computer and run this command in a terminal:

```sh
npx --yes web-push generate-vapid-keys --json
```

The command prints JSON containing a matching `publicKey` and `privateKey` pair. Copy only the text value for each key, without its quotes or the surrounding JSON. Generate a separate pair for production and development. Keep the output private. In the matching env file:

1. Set `PARKEREN_WEBPUSH_SUBJECT` to a contact URI, for example `mailto:beheer@example.com`.
2. Copy `publicKey` to `PARKEREN_WEBPUSH_PUBLIC_KEY`.
3. Copy `privateKey` to `PARKEREN_WEBPUSH_PRIVATE_KEY`.

The browser receives the public key from the authenticated app. The server uses the private key to sign notifications; never put it in frontend settings, source control, screenshots, or support messages. Keep the same pair when updating the app. Replacing it can require users to enable notifications again. Restrict access to the populated env file on the NAS.

## Production

Set `COMPOSE_PROJECT_NAME` to the exact project name of the current production stack; this is essential to reuse its named database volume. If the existing project name or volume is unknown, stop and ask the NAS owner rather than guessing.

Set `PARKEREN_APP_IMAGE` to an already-published immutable production release or commit-SHA reference. Do not use `dev`. The current placeholder is intentionally not pullable. Keep the production database password and TwoPark credentials in this private env file. For a brand-new database only, set the bootstrap administrator username and six-digit PIN; remove those two values after the first administrator is created.

Set `PARKEREN_APP_PORT` to the NAS host port for the app; it defaults to `5080` and maps to container port `8080`.

From the repository root, validate without printing secrets, then pull and start:

```sh
docker compose --env-file deploy/examples/.env.production -f deploy/examples/compose.production.example.yml config --quiet
docker compose --env-file deploy/examples/.env.production -f deploy/examples/compose.production.example.yml pull
docker compose --env-file deploy/examples/.env.production -f deploy/examples/compose.production.example.yml up -d
```

Before `up`, have the NAS owner confirm that the project name and database volume match the running stack. The app supports one active instance only: do not scale it or run overlapping deployments.

To roll back, change `PARKEREN_APP_IMAGE` to the previously deployed immutable SHA or release tag, then run `pull` and `up -d` again with the same project name and env file. Do not run `down -v`; it removes named volumes and can delete database data.

## Development

The development env template has its own password, Compose project name, and named volumes; keep those distinct from production. The default `dev` tag follows the latest successful develop publication. For a fixed pair of images, set `PARKEREN_DEV_IMAGE_TAG` to the same `dev-<commit-sha>` tag for both images.

```sh
docker compose --env-file deploy/examples/.env.development -f deploy/examples/compose.development.example.yml config --quiet
docker compose --env-file deploy/examples/.env.development -f deploy/examples/compose.development.example.yml pull
docker compose --env-file deploy/examples/.env.development -f deploy/examples/compose.development.example.yml up -d
```

The development stack uses the TwoPark mock and needs no real TwoPark credentials. Ports default to `5080` for the app and `5081` for the mock; change the corresponding env values if those ports are already in use. These examples have not been deployed to or validated on the NAS. NAS access and persistent-data operations remain with the NAS owner.