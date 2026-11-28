namespace PracticeManager.Core;

// Relative, display-only folder names. Never interpreted as a disk path by this template.
public sealed record ProjectFolderNode(string Name,IReadOnlyList<ProjectFolderNode> Children);
public static class ProjectFolderTemplate
{
 public const string Revision="CRC-Production-2";
 static ProjectFolderNode Node(string name,params ProjectFolderNode[] children)=>new(name,Array.AsReadOnly(children));
 public static IReadOnlyList<ProjectFolderNode> Production(bool includeCorrespondence=false)
 {
  var folders=new List<ProjectFolderNode>{
   Node("Architectural"),Node("Calculations"),Node("Construction Administration"),
   Node("Plancheck Comment Responses"),Node("QA-QC"),
   Node("Reference Documents"),
   Node("Send Outs"),Node("Site Pictures"),Node("Structural DWGs")
  };
  if(includeCorrespondence)folders.Insert(3,Node("Correspondence"));
  return folders.AsReadOnly();
 }
 // Restricted documents remain under a separate access boundary; no production descendants.
 public static IReadOnlyList<ProjectFolderNode> Restricted()=>Array.AsReadOnly(new[]{
  Node("Proposals"),Node("Agreements and Contracts"),Node("Change Orders and Additional Services"),Node("Billing Administration")
 });
}
