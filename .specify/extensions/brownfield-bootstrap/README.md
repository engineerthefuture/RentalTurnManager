# Brownfield Bootstrap Extension

> **spec-kit Extension** for bootstrapping existing projects into SDD workflow.

## Overview

This extension provides commands to bring **existing (brownfield) projects** into spec-kit's Specification-Driven Development (SDD) workflow with automatic project analysis and customized template generation.

**Important**: This extension enhances the core spec-kit by customizing it for existing projects. You must initialize spec-kit first before installing this extension.

## Installation

### For Existing Projects (Brownfield)

**Step 1: Initialize spec-kit** (creates core structure and commands):

```bash
# Navigate to your existing project directory
cd /path/to/your-existing-project

# Initialize spec-kit in the current directory
# This creates .specify/ directory and registers core commands
specify init --here --ai claude --force
```

**What this does:**
- ✅ Creates `.specify/` directory structure
- ✅ Downloads spec-kit templates from GitHub
- ✅ Registers core commands: `/speckit.specify`, `/speckit.plan`, `/speckit.tasks`, `/speckit.implement`, etc.
- ✅ Creates `.claude/commands/` directory with all core spec-kit commands

**Step 2: Install Brownfield Extension** (adds brownfield bootstrap):

```bash
# Install from GitHub (when available in catalog)
specify extension add brownfield-bootstrap

# Or install from local source
specify extension add --dev /path/to/spec-kit-brownfield-extensions/extension
```

**Step 3: Run Bootstrap** (customizes for your project):

```bash
# In Claude Code or your AI agent
/speckit.brownfield-bootstrap.init

# Or with specific focus
/speckit.brownfield-bootstrap.init Focus on the authentication module
```

**What the bootstrap does:**
- 🔍 Analyzes your existing codebase
- 📝 **Overwrites** `.specify/memory/constitution.md` with project-specific version
- 🎨 **Adapts** templates to your tech stack and coding style
- ✅ **Preserves** all core spec-kit commands

### Manual Installation (Not Recommended)

If you need manual installation, first run `specify init --here`, then copy:

```bash
mkdir -p .claude/commands
cp extension/commands/init.md .claude/commands/speckit.brownfield-bootstrap.init.md
cp extension/commands/init-cn.md .claude/commands/speckit.brownfield-bootstrap.init-cn.md
```

## Commands

| Command | Description |
|---------|-------------|
| `/speckit.brownfield-bootstrap.init` | Initialize SDD for existing brownfield projects (English) |
| `/speckit.brownfield-bootstrap.init-cn` | 初始化棕地项目的SDD工作流 (Chinese) |

### Aliases (Backward Compatibility)

- `speckit.brownfield.bootstrap` → `speckit.brownfield-bootstrap.init`
- `speckit.brownfield.bootstrap-cn` → `speckit.brownfield-bootstrap.init-cn`

## Usage

```bash
# Run the init command in your existing project
/speckit.brownfield-bootstrap.init

# Or with specific focus
/speckit.brownfield-bootstrap.init Focus on the user authentication module
```

## Generated Files

| File | Path | Description |
|------|------|-------------|
| Project Constitution | `.specify/memory/constitution.md` | Core principles and constraints |
| Spec Template | `.specify/templates/spec-template.md` | Feature specification template |
| Plan Template | `.specify/templates/plan-template.md` | Implementation plan template |
| Tasks Template | `.specify/templates/tasks-template.md` | Task breakdown template |

## Requirements

- spec-kit >= 0.1.0
- Required commands: `speckit.specify`, `speckit.plan`, `speckit.tasks`

## Related

- **Skills**: See the separate `skills/` directory for `brownfield-skills` and `brownfield-ears`
- **Main README**: See the repository root for complete documentation

## License

MIT License
