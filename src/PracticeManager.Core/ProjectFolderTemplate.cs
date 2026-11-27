namespace PracticeManager.Core;

public sealed record ProjectFolderTemplateItem(string Name,bool IncludedByDefault);

// Proposed defaults based on CRC's preferred terminology. Pure template
// validation and preview only: never creates, renames, moves or deletes folders.
public static class ProjectFolderTemplate
{
 public static IReadOnlyList<ProjectFolderTemplateItem> Defaults{get;}=Array.AsReadOnly(new[]{
  new ProjectFolderTemplateItem("Architectural",true),
  new ProjectFolderTemplateItem("Calculations",true),
  new ProjectFolderTemplateItem("Construction Administration",true),
  new ProjectFolderTemplateItem("Plancheck Comment Responses",true),
  new ProjectFolderTemplateItem("Reference Documents",true),
  new ProjectFolderTemplateItem("Send Outs",true),
  new ProjectFolderTemplateItem("Site Pictures",true),
  new ProjectFolderTemplateItem("Structural DWGs",true),
  new ProjectFolderTemplateItem("Correspondence",false),
  new ProjectFolderTemplateItem("QA-QC",false),
  new ProjectFolderTemplateItem("Mechanical",false),
  new ProjectFolderTemplateItem("Electrical",false),
  new ProjectFolderTemplateItem("Plumbing",false)
 });
 public static IReadOnlyList<string> Preview(IReadOnlyList<ProjectFolderTemplateItem> template)
 {
  if(template.Count is <1 or >32)throw new ArgumentException("Select a bounded project folder template.");
  var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var item in template)
  {
   var n=item.Name;
   if(string.IsNullOrWhiteSpace(n)||n.Length>100||n!=n.Trim()||n.EndsWith('.')||
      n.Any(ch=>char.IsControl(ch)||ch is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')||
      System.Text.RegularExpressions.Regex.IsMatch(n.Split('.')[0].Trim(),@"^(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])$",System.Text.RegularExpressions.RegexOptions.IgnoreCase|System.Text.RegularExpressions.RegexOptions.CultureInvariant)||
      !names.Add(n))
    throw new ArgumentException("Use unique, plain Windows-compatible folder names.");
  }
  var selected=template.Where(x=>x.IncludedByDefault).Select(x=>x.Name).ToArray();
  if(selected.Length==0)throw new ArgumentException("Select at least one folder.");
  return Array.AsReadOnly(selected);
 }
}
