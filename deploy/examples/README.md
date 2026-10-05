# Published image examples

These files are examples for pulling the public GHCR images. They do not replace the active Compose files under `deploy/` and must not be used to change a running NAS stack until its owner confirms the current Compose project name and configuration.

## Production

Copy `.env.production.example` to a private `.env.production` on the NAS. Replace the example project name with the exact project name used by the existing production stack and replace every `replace-with-...` value. This preserves the existing named database volume. Keep the populated env file out of Git and restrict access to it.

Set `PARKEREN_APP_IMAGE` to a published immutable release tag or commit-SHA tag. Do not use `dev` for production. No production release image is available until a release tag has been published; do not pull the placeholder value.

From the repository root, inspect the resolved configuration before applying it:

```sh
docker compose --env-file deploy/examples/.env.production -f deploy/examples/compose.production.example.yml config
docker compose --env-file deploy/examples/.env.production -f deploy/examples/compose.production.example.yml pull
docker compose --env-file deploy/examples/.env.production -f deploy/examples/compose.production.example.yml up -d
```

Before `up`, compare the resolved project name, services, and volume with the current deployment. Do not proceed if the existing project/volume identity is unknown. This example preserves the one-active-app-instance requirement; do not scale the app or use overlapping deployments.

To roll back, set `PARKEREN_APP_IMAGE` to the previously deployed immutable SHA or release tag, then run `pull` and `up -d` again with the same project name and env file.

## Development

Copy `.env.development.example` to a private `.env.development` and replace the database-password placeholder. The default `dev` tag follows the latest successful develop publication; for a fixed pair of images, set `PARKEREN_DEV_IMAGE_TAG` to the same `dev-<commit-sha>` tag for both images. The Compose project name and named volumes are isolated from production.

```sh
docker compose --env-file deploy/examples/.env.development -f deploy/examples/compose.development.example.yml config
docker compose --env-file deploy/examples/.env.development -f deploy/examples/compose.development.example.yml pull
docker compose --env-file deploy/examples/.env.development -f deploy/examples/compose.development.example.yml up -d
```

This development stack uses the TwoPark mock and does not require real TwoPark credentials. These examples have not been deployed to or validated on the NAS; NAS access and persistent-data operations remain with the NAS owner.