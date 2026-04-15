# Extension AI Installation Guide

This document provides executable instructions for AI agents to install the Brownfield Bootstrap Extension.

## Prerequisites

- spec-kit CLI installed (`uv tool install specify-cli`)
- Git available
- Target project directory (existing brownfield project)

## Critical Workflow Requirements

⚠️ **IMPORTANT**: This extension requires the core spec-kit to be initialized first. The workflow is:

1. **First**: `specify init --here` (creates `.specify/` directory and core commands)
2. **Then**: Install this extension (adds brownfield bootstrap commands)
3. **Finally**: Run `/speckit.brownfield-bootstrap.init` (customizes for your project)

## Installation Steps

### Step 1: Initialize Spec-Kit Core

Navigate to the existing project directory and initialize spec-kit:

```bash
cd /path/to/existing-project

# Initialize spec-kit in the current directory
# This creates .specify/ directory and registers ALL core commands
specify init --here --ai claude --force
```

**What this does:**
- Creates `.specify/` directory structure (required by extension system)
- Downloads spec-kit templates from GitHub
- Registers core commands: `/speckit.specify`, `/speckit.plan`, `/speckit.tasks`, `/speckit.implement`, etc.
- Creates `.claude/commands/` with all core spec-kit commands

**Why this is required:**
- `specify extension add` requires `.specify/` directory to exist
- Core spec-kit commands come from the GitHub template, not the extension
- Extension commands work alongside core commands, not replace them

### Step 2: Clone Extension Repository

```bash
git clone https://github.com/wcpaxx/spec-kit-brownfield-extensions.git /tmp/brownfield-ext
```

### Step 3: Install Brownfield Extension

**Recommended: Using spec-kit Extension System**

```bash
specify extension add --dev /tmp/brownfield-ext/extension
```

This will:
- Copy extension files to `.specify/extensions/brownfield-bootstrap/`
- Register brownfield commands in `.claude/commands/`
- **Preserve** all existing core spec-kit commands

**Alternative: Manual Installation (Not Recommended)**

```bash
mkdir -p .claude/commands
cp /tmp/brownfield-ext/extension/commands/init.md .claude/commands/speckit.brownfield-bootstrap.init.md
cp /tmp/brownfield-ext/extension/commands/init-cn.md .claude/commands/speckit.brownfield-bootstrap.init-cn.md
```

### Step 4: Verify Installation

Check that **both** core and extension commands are available:

```bash
# Check core commands (from spec-kit init)
ls .claude/commands/speckit.specify.md
ls .claude/commands/speckit.plan.md
ls .claude/commands/speckit.tasks.md

# Check extension commands (from brownfield extension)
ls .claude/commands/speckit.brownfield-bootstrap*.md
```

Expected output:
- ✅ Core commands: `speckit.specify.md`, `speckit.plan.md`, `speckit.tasks.md`, etc.
- ✅ Extension commands: `speckit.brownfield-bootstrap.init.md`, `speckit.brownfield-bootstrap.init-cn.md`

### Step 5: Cleanup

```bash
rm -rf /tmp/brownfield-ext
```

## Post-Installation

After installation, the following commands are available:

| Command | Description |
|---------|-------------|
| `/speckit.brownfield-bootstrap.init` | Bootstrap SDD for brownfield projects (English) |
| `/speckit.brownfield-bootstrap.init-cn` | 初始化棕地项目的SDD工作流 (Chinese) |

Choose one command based on your preferred language. Both commands are functionally identical.

## Usage Workflow

### 1. Run Bootstrap Command

In Claude Code or your AI agent:

```bash
# English version
/speckit.brownfield-bootstrap.init

# Or Chinese version
/speckit.brownfield-bootstrap.init-cn

# Or with specific focus
/speckit.brownfield-bootstrap.init Focus on the authentication module
```

**What the bootstrap does:**
- 🔍 Scans your existing codebase structure
- 📊 Analyzes tech stack, coding conventions, and architectural patterns
- 📝 **Overwrites** `.specify/memory/constitution.md` with project-specific constitution
- 🎨 **Adapts** templates (spec/plan/tasks) to match your project's style
- ✅ **Preserves** all core spec-kit commands

### 2. Review Generated Configuration

Check the customized files:

```bash
# Review project-specific constitution
cat .specify/memory/constitution.md

# Review adapted templates
cat .specify/templates/spec-template.md
cat .specify/templates/plan-template.md
cat .specify/templates/tasks-template.md
```

### 3. Use Standard SDD Workflow

Now use the core spec-kit commands with your customized configuration:

```bash
# Create feature specification
/speckit.specify

# Create implementation plan
/speckit.plan

# Generate task breakdown
/speckit.tasks

# Execute implementation
/speckit.implement
```

All these commands will use your project-specific constitution and templates.

## Skills Installation

For AI development expertise skills, see the separate skills directory:
- `skills/AI-INSTALL-GUIDE.md`

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Command not found | Ensure files are in `.claude/commands/` |
| Permission denied | Run with appropriate permissions |
| spec-kit not found | Install spec-kit first |
