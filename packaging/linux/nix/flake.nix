# ArchoeraCX NixOS packaging entry point.
#
# The workflow stages this file next to package.nix, bundle/, the desktop entry,
# AppStream metadata and the 512px icon (see packaging/linux/package.sh nix),
# then runs:
#   nix build "path:$PWD#default" --option sandbox false
#
# path: is deliberate: the staged directory is git-ignored, and plain flake
# references inside a Git repository would only include tracked files.
#
# The bundle is the prebuilt Flutter/FFI tree. autoPatchelfHook rewrites the
# interpreter and store RUNPATHs for NixOS; the bundle's own relative layout is
# preserved.
{
  description = "ArchoeraCX — open-source Minecraft: Java Edition launcher";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self
    , nixpkgs
    }:
    let
      systems = [ "x86_64-linux" ];
      forAllSystems = nixpkgs.lib.genAttrs systems;
    in
    {
      packages = forAllSystems (system:
        let
          pkgs = import nixpkgs { inherit system; };
        in
        {
          default = pkgs.callPackage ./package.nix { };
          archoera-cx = self.packages.${system}.default;
        });
    };
}
