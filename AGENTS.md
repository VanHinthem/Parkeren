# Agent instructions

## Branch and review workflow

Before implementation, ensure each feature has a GitHub issue describing its scope and acceptance criteria. For a larger initiative, use one tracking issue and separate issues for independently reviewable features. Include the issue number in the feature branch name, create the branch from the latest `develop`, and never commit directly to `develop`. Target the pull request at `develop`, link the issue, and leave it open for the user to review and complete. Do not merge, squash, or enable auto-merge on the user's behalf.

For work on container images, CI/CD, release tags, or database migrations related to releases, read [the container image pipeline dossier](docs/work/container-image-pipeline.md) before making changes. It is the single source of truth for decisions, scope, phase acceptance criteria, and the current checkpoint.

Continue only the next incomplete phase. After each phase, run and record the relevant validation and update the dossier checkpoint. Do not deploy to the NAS, configure backups, or reset persistent data as part of this work; stop and ask when an operation could affect persistent data or needs NAS access.