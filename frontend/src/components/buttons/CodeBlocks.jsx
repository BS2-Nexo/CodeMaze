export default function CodeBlockButton ({
  code,
  type,
  handleDragStart,
  addCodeBlock
}) {
    const typeClasses = {
  KEYWORDS: "bg-[#F9AB00]",
  DIRS: "bg-[#35B23A]",
  SENSORS: "bg-[#639BFF]"
};

  return (
    <button
      key={code}
      type="button"
      draggable
      onDragStart={(event) => handleDragStart(event, code)}
      onClick={() => addCodeBlock(code)}
      aria-label="Add code block"
 className={`m-0.5 p-1.5 text-[10px] cursor-grab font-semibold text-center text-black active:cursor-grabbing ${
  typeClasses[type] ?? "bg-gray-200"
}`}
    >
      {code}
    </button>
  );
}