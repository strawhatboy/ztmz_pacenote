{ Keep precedence explicit: a command-line destination wins over saved paths. }
function ResolveInstallDirectory(const CommandLineDir, RegisteredDir,
  PreviousDir, DefaultDir: String): String;
begin
  if Trim(CommandLineDir) <> '' then
    Result := CommandLineDir
  else if Trim(RegisteredDir) <> '' then
    Result := RegisteredDir
  else if Trim(PreviousDir) <> '' then
    Result := PreviousDir
  else
    Result := DefaultDir;
end;
