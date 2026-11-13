# Blocks Menu

Blocks Menu is a unified production catalog and order-management interface for RimWorld 1.6.

## Features

- Browse production recipes and active production orders in one interface.
- Organize recipes into built-in and custom folders.
- Configure folder workbenches and individually included or excluded recipes.
- Create separate native RimWorld Bills on selected compatible workbenches.
- Configure production modes, target stock, quantities, and resume thresholds.
- Browse clothing and workwear catalogs, including equipment bonuses.
- View recipe ingredients, compatible workbenches, and order details.

## Build

Open `BlocksMenu.csproj` in Visual Studio with the .NET Framework 4.7.2 targeting pack installed. The project references the RimWorld assemblies in `Lib/`. Build the Release configuration.

This source package contains updated English UI strings. Rebuild the project to generate a DLL containing these changes.
