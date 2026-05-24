// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.Sql;

namespace SectorEditorAvalonia.Utilities
{
    // Wraps the existing FactionSql DAO behind the IFactionLookup
    // interface so sprite / dialog code doesn't have to know how
    // factions are sourced. The MainWindow constructs one FactionSql,
    // wraps it here, and installs the result into EditorGlobals.Factions
    // — the rest of the editor reaches through that.
    public sealed class FactionLookupAdapter : IFactionLookup
    {
        private readonly FactionSql _dao;

        public FactionLookupAdapter(FactionSql dao) { _dao = dao; }

        public string FindNameById(int id) => _dao.findNameByID(id);
        public int FindIdByName(string name) => _dao.findIDbyName(name);
    }
}
