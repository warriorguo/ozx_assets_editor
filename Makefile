# OZX Assets Editor — top-level Make targets.
#
# Day-to-day development still uses `dotnet build` / `dotnet run --project OAE.App`
# directly. These targets just wrap the packaging script.

.PHONY: app app-clean test

# Build the macOS .app bundle for the host architecture.
# Override RID to cross-publish: `make app RID=osx-x64`.
app:
	./packaging/macos/build.sh

# Remove the packaged bundle output.
app-clean:
	rm -rf packaging/macos/dist

# Run xUnit tests.
test:
	dotnet test OAE.slnx --nologo
