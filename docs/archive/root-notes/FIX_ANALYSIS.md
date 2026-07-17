// Fix the ShowSettings method in ZhuaQianDesktop.cs
// The method needs to be refactored to separate UI display from model switching

// Problem found in ZhuaQianDesktop.cs
// 1. Sidebar button click handler (line 355): calls ShowSettings() which immediately does model switching
// 2. Main menu button click handler (line 515): same issue
// 3. ShowSettings() method performs model switching right after dialog closes

// Solution: Split into two separate methods
// 1. ShowSettingsDialog() - Just shows the UI dialog without any model switching
// 2. ShowSettings() - Only processes model switching after dialog closes (existing logic preserved)

// The fix involves:
// 1. Creating a new ShowSettingsDialog method that only displays the SettingsDialog
// 2. Updating button click handlers to use ShowSettingsDialog
// 3. Keeping the existing ShowSettings method for backward compatibility (but mark as deprecated)

// This ensures users can see all settings features (language, API keys, model selection) without immediate model switching