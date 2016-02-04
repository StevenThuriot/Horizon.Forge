using System;

namespace Horizon.Forge
{
    public interface IMemberDefinition
    {
        bool IsVirtual { get; }
        string Name { get; }
        Type MemberType { get; }
        void Build(System.Reflection.Emit.TypeBuilder typeBuilder);
    }
}
