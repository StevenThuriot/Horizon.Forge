using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Horizon.Forge
{
    class FieldMemberDefinition : MemberDefinition<FieldBuilder>
    {        
        public FieldMemberDefinition(string name, Type type) 
            : base(name, type, false)
        {
        }

        internal override FieldBuilder Build(System.Reflection.Emit.TypeBuilder typeBuilder)
        {
            return typeBuilder.DefineField(Name, MemberType, FieldAttributes.Public);
        }
    }
}