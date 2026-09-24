; ===========================================================================
;  Hello World - EXE (segmented) version
; ===========================================================================
;  In EXE programs code, data and the stack live in separate SEGMENTs.
;  This is a tidier layout for bigger programs.
; ===========================================================================

#make_EXE#

; ---------------------------------------------------------------------------
;  Data segment: variables are defined here
; ---------------------------------------------------------------------------
data segment
    message db 'Hello, World! (EXE)', 13, 10, '$'
ends

; ---------------------------------------------------------------------------
;  Stack segment: used by PUSH/POP and CALL/RET
; ---------------------------------------------------------------------------
stack segment
    dw 128 dup(0)           ; 128 words (256 bytes) of free space
ends

; ---------------------------------------------------------------------------
;  Code segment
; ---------------------------------------------------------------------------
code segment
start:
    ; When an EXE starts, DS does not point to our data; we set it.
    mov ax, data            ; AX <- address of the data segment
    mov ds, ax              ; DS <- AX  (a constant cannot be moved into
                            ;           a segment register directly)

    lea dx, message         ; LEA: load the address of message into DX
    mov ah, 09h             ; DOS: print string
    int 21h

    mov ax, 4C00h           ; AH=4Ch (terminate), AL=00h (exit code)
    int 21h
ends

end start                   ; Execution begins at the 'start' label
